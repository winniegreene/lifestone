// Lifestone terrain: retail TexMerge composite, evaluated per pixel.
//
// Spec: docs/terrain-shader.md (the per-cell recipe, the fragment algorithm, colour space) and docs/terrain-renderer.md
// (inputs, mip policy, deviations). Retail functions are cited by name from the Mag-nus/acclient-AI-RE decompile of
// acclient 11.4186; no decompiled code is reproduced. The CPU reference is Lifestone.Data's TerrainCompositor; the
// golden test (tools/terrain-golden.ps1) compares debug_view 1 and 4 against it.
//
// Retail builds one merged 1024^2 texture per cell palette code on the CPU (TexMerge::FillTempTexBuffer:
// ImgTex::TileCSI then ImgTex::MergeTexture per overlay and road) and draws the cell with it, MODULATE'd by
// CLandBlockStruct::calc_lighting vertex colours, through D3D linear range fog. This shader computes the same function
// of (cell, u, v) from two texture arrays and per-cell layer indices.
shader_type spatial;
render_mode unshaded, fog_disabled, cull_back, depth_draw_opaque, depth_test_default;

// ---------------------------------------------------------------------------------------------------------------------
// Lighting and fog inputs: the render contract's GLOBAL uniforms (docs/render-contract.md §1), declared in project.godot
// [shader_globals] and driven by RenderGlobals (noon defaults now, the sky track's time of day later), so one driver
// lights and fogs terrain and objects alike. Terrain reads ac_land_ambient (CLandBlockStruct::calc_lighting:
// ambient_color * ambient_level); objects read ac_ambient (LScape::calc_object_light), which terrain must not use.
// Colours are raw retail values (byte * 0.0039215689, no sRGB decode); directions are in Godot axes.
// (Until the M1 terrain integration these were material uniforms behind an AC_GLOBAL_UNIFORMS switch.)
// ---------------------------------------------------------------------------------------------------------------------
global uniform vec3 ac_sun_dir;          // unit vector towards the sun (LScape::sunlight normalised)
global uniform vec3 ac_sun_color;        // sunlight_color * |sunlight| (calc_lighting's sun term)
global uniform vec3 ac_land_ambient;     // ambient_color * ambient_level (level floored at 0.2)
global uniform vec3 ac_fog_color;        // D3DRS_FOGCOLOR
global uniform float ac_fog_start;       // D3DRS_FOGSTART (m)
global uniform float ac_fog_end;         // D3DRS_FOGEND (m)
global uniform bool ac_fog_enabled;      // world fog on
global uniform float ac_max_mip_level;   // retail creates at most 4 mip levels (ImgTex::CreateD3DTexture): 3
global uniform bool ac_byte_space;       // render-fidelity fix: output raw bytes; the frame is converted once (ByteSpaceBlending)

// ---------------------------------------------------------------------------------------------------------------------
// Texture arrays (TerrainAtlasManifest order). Sampled WITHOUT source_color: every step works on raw bytes, as retail.
// The *_nearest samplers point at the same arrays for the golden views (debug_view 1 and 4).
// ---------------------------------------------------------------------------------------------------------------------
uniform sampler2DArray terrain_layers : filter_linear_mipmap, repeat_enable;          // D3D LINEAR min/mag/mip, WRAP
uniform sampler2DArray alpha_layers : filter_linear_mipmap, repeat_disable;           // alpha maps cover one cell: CLAMP
uniform sampler2DArray terrain_layers_nearest : filter_nearest, repeat_enable;
uniform sampler2DArray alpha_layers_nearest : filter_nearest, repeat_disable;

uniform float layer_tiling[64];          // TerrainTex::tex_tiling per colour layer (2 in Dereth: a texture repeats every 12 m)
uniform float layer_lod_offset[64];      // log2(BaseTexSize / (layer size * tiling)) per colour layer (0 in Dereth)
uniform float alpha_lod_offset = 1.0;    // log2(BaseTexSize / alpha size): retail merges alpha maps upscaled 512 -> 1024

// 0 = retail (lit, fogged)                       2 = raw blend, filtered (no light, no fog)
// 1 = raw blend, nearest, level 0 (golden)       3 = vertex light only
// 4 = lit raw blend, nearest, level 0, no fog (golden lighting)
// 5 = fog factor (white = no fog)                6 = cell layer count (overlays + roads, 0 black .. 5 white)
uniform int debug_view = 0;

// ACRender::landPolyDraw lowers every terrain vertex by zFightTerrainAdjust (0.01 m) when it submits the triangles.
uniform float z_fight_adjust = 0.01;

varying flat ivec4 v_tex;     // base, overlay0, overlay1, overlay2 colour layers (255 = unused)
varying flat ivec4 v_alpha;   // overlay0, overlay1, overlay2, road0 alpha layers (255 = unused)
varying flat ivec4 v_misc;    // road1 alpha, rot0 | rot1 << 2 | rot2 << 4 | roadRot0 << 6, roadRot1 | flags << 2, road layer
varying vec3 v_light;         // calc_lighting vertex colour as landPolyDraw submits it (truncated bytes / 255)
varying float v_fog;          // D3D linear range fog factor (1 = no fog), per vertex as FOGVERTEXMODE LINEAR

int byte_of(float x) {
	return int(round(x * 255.0));
}

// docs/terrain-shader.md §6: one exact sRGB decode at the end, so the Linear tonemapper re-encodes retail's bytes.
vec3 ac_srgb_to_linear(vec3 c) {
	return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
}

void vertex() {
	v_tex = ivec4(byte_of(CUSTOM0.r), byte_of(CUSTOM0.g), byte_of(CUSTOM0.b), byte_of(CUSTOM0.a));
	v_alpha = ivec4(byte_of(CUSTOM1.r), byte_of(CUSTOM1.g), byte_of(CUSTOM1.b), byte_of(CUSTOM1.a));
	v_misc = ivec4(byte_of(CUSTOM2.r), byte_of(CUSTOM2.g), byte_of(CUSTOM2.b), byte_of(CUSTOM2.a));

	// CLandBlockStruct::calc_lighting: rgb = min(1, sunColor/255 * max(0, n . sunlight) + ambColor/255 * ambLevel), no
	// lower clamp. With sunlight = |sunlight| * ac_sun_dir the sun term is ac_sun_color * max(0, n . ac_sun_dir).
	vec3 n = normalize(MODEL_NORMAL_MATRIX * NORMAL);
	float d = max(dot(n, ac_sun_dir), 0.0);
	vec3 light = min(ac_sun_color * d + ac_land_ambient, vec3(1.0));
	v_light = floor(light * 255.0) / 255.0;   // ACRender::landPolyDraw sends (uint8)(c * 255.0), alpha 0xFF

	// RenderDeviceD3D::SetDefaultD3DStates: FOGVERTEXMODE LINEAR, RANGEFOGENABLE: f = (end - d) / (end - start), d the
	// eye-to-vertex distance, interpolated across the triangle.
	float dist = length((MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).xyz);
	v_fog = ac_fog_enabled ? clamp((ac_fog_end - dist) / max(ac_fog_end - ac_fog_start, 1e-4), 0.0, 1.0) : 1.0;

	VERTEX.y -= z_fight_adjust;
}

// Alpha-map UV for a rotation (docs/terrain-shader.md §5; ImgTex::MergeTexture start offsets and strides).
vec2 rot_uv(int r, vec2 uv) {
	if (r == 1) {
		return vec2(1.0 - uv.y, uv.x);
	} else if (r == 2) {
		return vec2(1.0 - uv.x, 1.0 - uv.y);
	} else if (r == 3) {
		return vec2(uv.y, 1.0 - uv.x);
	}
	return uv;
}

// Fraction of what is underneath that retail keeps for an alpha byte a8: (a8 + (a8 > 128)) / 256 (MergeTexture's
// a' = a + (a > 128), dst = (a' dst + (256 - a') src) >> 8), continuous under filtering.
float retail_keep(float a) {
	float a8 = a * 255.0;
	return (a8 + clamp(a8 - 128.0, 0.0, 1.0)) / 256.0;
}

vec3 layer_rgb(int layer, vec2 uv, float merged_lod, bool nearest) {
	vec3 p = vec3(uv * layer_tiling[layer], float(layer));
	if (nearest) {
		return textureLod(terrain_layers_nearest, p, 0.0).rgb;
	}
	return textureLod(terrain_layers, p, max(merged_lod - layer_lod_offset[layer], 0.0)).rgb;
}

float alpha_at(int layer, int rotation, vec2 uv, float merged_lod, bool nearest) {
	vec3 p = vec3(rot_uv(rotation, uv), float(layer));
	if (nearest) {
		return textureLod(alpha_layers_nearest, p, 0.0).r;
	}
	return textureLod(alpha_layers, p, max(merged_lod - alpha_lod_offset, 0.0)).r;
}

// One MergeTexture step: keep W(a) of what is underneath, take the rest from the layer. Texels with a = 255 are skipped
// (W = 1), so the layer is not sampled there.
vec3 merge(vec3 c, int layer, int alpha_layer, int rotation, vec2 uv, float merged_lod, bool nearest) {
	float keep = retail_keep(alpha_at(alpha_layer, rotation, uv, merged_lod, nearest));
	if (keep >= 1.0) {
		return c;
	}
	return mix(layer_rgb(layer, uv, merged_lod, nearest), c, keep);
}

void fragment() {
	bool nearest = debug_view == 1 || debug_view == 4;

	// Level of detail of retail's merged BaseTexSize^2 cell texture at this pixel, clamped to the levels retail creates.
	// alpha_layers is sampled at the plain cell UV, so its LOD + alpha_lod_offset is the merged texture's LOD.
	float merged_lod = clamp(textureQueryLod(alpha_layers, UV).y + alpha_lod_offset, 0.0, ac_max_mip_level);

	// TileCSI: the base repeated tex_tiling times. Then overlays 0..2 and roads 0..1, in order, stopping at the first
	// unused slot (TexMerge::FillTempTexBuffer).
	vec3 c = layer_rgb(v_tex.x, UV, merged_lod, nearest);
	int rots = v_misc.y;
	int count = 0;
	if (v_tex.y != 255) {
		c = merge(c, v_tex.y, v_alpha.x, rots & 3, UV, merged_lod, nearest);
		count++;
		if (v_tex.z != 255) {
			c = merge(c, v_tex.z, v_alpha.y, (rots >> 2) & 3, UV, merged_lod, nearest);
			count++;
			if (v_tex.w != 255) {
				c = merge(c, v_tex.w, v_alpha.z, (rots >> 4) & 3, UV, merged_lod, nearest);
				count++;
			}
		}
	}
	if (v_alpha.w != 255) {
		c = merge(c, v_misc.w, v_alpha.w, (rots >> 6) & 3, UV, merged_lod, nearest);
		count++;
		if (v_misc.x != 255) {
			c = merge(c, v_misc.w, v_misc.x, v_misc.z & 3, UV, merged_lod, nearest);
			count++;
		}
	}

	if (debug_view == 0) {
		c *= v_light;                       // fixed-function MODULATE with the lit vertex colour
		c = mix(ac_fog_color, c, v_fog);    // D3D fog blends towards the fog colour, still in retail's byte space
	} else if (debug_view == 3) {
		c = v_light;
	} else if (debug_view == 4) {
		c *= v_light;
	} else if (debug_view == 5) {
		c = vec3(v_fog);
	} else if (debug_view == 6) {
		c = vec3(float(count) / 5.0);
	}

	// docs/render-contract.md §2.3: raw bytes in byte space (the main view converts the whole frame once, after every
	// blend), else the exact decode the Linear tonemapper re-encodes. The terrain preview and its golden test never
	// select byte space, so they take the second branch.
	ALBEDO = ac_byte_space ? c : ac_srgb_to_linear(c);
}
