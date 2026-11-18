using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Lifestone.WorldWalk;

/// <summary>The world as a picture (W1 and W2): one page, one pixel block per landblock, north up. It derives from the dats, so it stays in the run's folder under cache/ and is never committed.</summary>
internal static class MapPage
{
    private const int Side = 255;

    public static int Run(WorldWalkOptions options)
    {
        if (options.CensusRun is null)
        {
            Console.Error.WriteLine("map needs --census=DIR");
            return 2;
        }

        var kinds = new int[Side * Side];
        foreach (var row in Rows(options.CensusRun))
        {
            var r = row.GetProperty("row");
            var cells = r.GetProperty("Cells").GetString();
            var water = r.GetProperty("Water").GetString();
            var kind = cells switch
            {
                "Dungeon" => 4,
                "Mixed" or "BuildingInteriors" => 5,
                _ => water switch { "sea" => 1, "coast" => 2, "land" => 3, _ => 0 },
            };
            kinds[IndexOf(row)] = kind;
        }

        var landmass = new int[Side * Side];
        var area = new int[Side * Side];
        var masses = "[]";
        if (options.GraphRun is { } graph)
        {
            foreach (var row in Rows(graph))
            {
                landmass[IndexOf(row)] = row.GetProperty("landmass").GetInt32();
                area[IndexOf(row)] = row.GetProperty("area").GetInt32();
            }

            masses = File.ReadAllText(Path.Combine(graph, "landmasses.json"));
        }

        var walks = new int[Side * Side];
        if (options.WalksRun is { } walksRun)
        {
            foreach (var row in Rows(walksRun))
            {
                walks[IndexOf(row)] = WalkVerdict(row);
            }
        }

        var physics = new int[Side * Side];
        if (options.OracleRun is { } oracleRun)
        {
            foreach (var row in Rows(oracleRun))
            {
                physics[IndexOf(row)] = PhysicsVerdict(row);
            }
        }

        Directory.CreateDirectory(options.Out);
        var page = Template
            .Replace("/*WALKS*/", options.WalksRun is null ? string.Empty : string.Join(',', walks), StringComparison.Ordinal)
            .Replace("/*PHYSICS*/", options.OracleRun is null ? string.Empty : string.Join(',', physics), StringComparison.Ordinal)
            .Replace("/*KINDS*/", string.Join(',', kinds), StringComparison.Ordinal)
            .Replace("/*MASS*/", string.Join(',', landmass), StringComparison.Ordinal)
            .Replace("/*AREA*/", string.Join(',', area), StringComparison.Ordinal)
            .Replace("/*MASSES*/", masses, StringComparison.Ordinal);
        var path = Path.Combine(options.Out, "map.html");
        File.WriteAllText(path, page, new UTF8Encoding(false));
        Console.WriteLine(path);
        return 0;
    }

    /// <summary>0 nothing to walk; 1 every walk clean; 2 arrived, but with a recovery move or a refused check; 3 a walk failed.</summary>
    private static int WalkVerdict(JsonElement row)
    {
        if (!row.TryGetProperty("edges", out var edges) || edges.GetArrayLength() == 0)
        {
            return 0;
        }

        var verdict = 1;
        foreach (var edge in edges.EnumerateArray())
        {
            if (!edge.TryGetProperty("bad", out var bad))
            {
                continue;
            }

            foreach (var walk in bad.EnumerateArray())
            {
                verdict = Math.Max(verdict, walk.GetProperty("reason").ValueKind == JsonValueKind.String ? 3 : 2);
            }
        }

        return verdict;
    }

    /// <summary>0 not scanned; 1 agrees (blocked margin round objects is by design); 2 small: slivers, trapped or refused points; 3 the map and the body disagree between large areas.</summary>
    private static int PhysicsVerdict(JsonElement row)
    {
        if (!row.TryGetProperty("counts", out var counts))
        {
            return 0;
        }

        static bool Has(JsonElement c, string kind) => c.TryGetProperty(kind, out _);
        return Has(counts, "closed-passage") || Has(counts, "joined-areas") ? 3
            : Has(counts, "joined-sliver") || Has(counts, "refused-on-open") || Has(counts, "trapped-on-open") ? 2
            : 1;
    }

    private static IEnumerable<JsonElement> Rows(string folder)
    {
        foreach (var line in File.ReadLines(Path.Combine(folder, "results.jsonl")))
        {
            using var doc = JsonDocument.Parse(line);
            yield return doc.RootElement.Clone();
        }
    }

    private static int IndexOf(JsonElement row)
    {
        var id = ushort.Parse(row.GetProperty("id").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((id >> 8) * Side) + (id & 0xFF);
    }

    private const string Template = """
<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>World walk map</title>
<style>
:root{--bg:#f7f6f2;--ink:#1d2329;--muted:#5d6670;--line:#d8d6cf}
@media (prefers-color-scheme:dark){:root{--bg:#15191d;--ink:#e8e6e1;--muted:#a0a8b0;--line:#333a41}}
body{margin:0;padding:16px;background:var(--bg);color:var(--ink);font:15px/1.45 system-ui,sans-serif}
h1{font-size:20px;margin:0 0 4px}p{margin:0 0 12px;color:var(--muted);max-width:70ch}
.bar{display:flex;gap:8px;flex-wrap:wrap;margin:0 0 12px}button{font:inherit;padding:6px 12px;border:1px solid var(--line);border-radius:6px;background:transparent;color:inherit;cursor:pointer}
button[aria-pressed=true]{background:var(--ink);color:var(--bg)}
canvas{display:block;width:100%;max-width:765px;height:auto;image-rendering:pixelated;border:1px solid var(--line);cursor:crosshair}
#info{min-height:1.5em;margin:8px 0;font-variant-numeric:tabular-nums}.legend{display:flex;gap:14px;flex-wrap:wrap;margin:8px 0;color:var(--muted)}.legend i{display:inline-block;width:12px;height:12px;margin-right:5px;vertical-align:-1px}
table{border-collapse:collapse;margin-top:12px;font-variant-numeric:tabular-nums}th,td{padding:3px 12px 3px 0;text-align:right;border-bottom:1px solid var(--line)}th:first-child,td:first-child{text-align:left}
</style></head><body>
<h1>World walk map</h1><p>Every landblock of the world, north up. Hover to read one.</p>
<div class="bar"><button id="kind" aria-pressed="true">Kinds</button><button id="mass" aria-pressed="false">Landmasses</button><button id="area" aria-pressed="false">Areas</button><button id="walk" aria-pressed="false">Walks</button><button id="phys" aria-pressed="false">Physics</button></div>
<canvas id="c" width="255" height="255"></canvas><div id="info">&nbsp;</div><div class="legend" id="legend"></div><table id="t"></table>
<script>
const K=[/*KINDS*/],M=[/*MASS*/],A=[/*AREA*/],T=/*MASSES*/,W=[/*WALKS*/],P=[/*PHYSICS*/];
const VC=['#26303a','#4f9d69','#e0b13c','#d1493f'],VW=['Nothing to walk','All walks clean','Arrived, with a recovery move or a refused check','A walk failed'],VP=['Not scanned','Map and body agree','Small: slivers, trapped or refused points','Map and body disagree between large areas'];
const L=n=>n.map((t,i)=>`<span><i style="background:${VC[i]}"></i>${t}</span>`).join('');const ALL=['kind','mass','area','walk','phys'];
if(!W.length)document.getElementById('walk').hidden=true;if(!P.length)document.getElementById('phys').hidden=true;
const C=['#000000','#264b6e','#5b8fa8','#7f9a5b','#b5533c','#d9a441'];const N=['No record','Sea','Coast','Land','Dungeon','Buildings or cells'];
const c=document.getElementById('c'),g=c.getContext('2d'),im=g.createImageData(255,255);let mode='kind';
const hx=s=>[1,3,5].map(i=>parseInt(s.slice(i,i+2),16));
const hue=n=>{if(n===0)return[38,75,110];if(n===1)return[127,154,91];const h=(n*137.5)%360,a=.55,l=.55,f=k=>{const m=(k+h/30)%12;return Math.round(255*(l-a*Math.min(l,1-l)*Math.max(-1,Math.min(m-3,9-m,1))))};return[f(0),f(8),f(4)]};
function draw(){for(let x=0;x<255;x++)for(let y=0;y<255;y++){const i=x*255+y,p=((254-y)*255+x)*4,rgb=mode==='kind'?hx(C[K[i]]):mode==='walk'?hx(VC[W[i]]):mode==='phys'?hx(VC[P[i]]):hue(mode==='mass'?M[i]:A[i]);im.data.set([rgb[0],rgb[1],rgb[2],255],p)}g.putImageData(im,0,0);
document.getElementById('legend').innerHTML=mode==='walk'?L(VW):mode==='phys'?L(VP):mode==='kind'?N.slice(1).map((n,i)=>`<span><i style="background:${C[i+1]}"></i>${n}</span>`).join(''):'<span>Each colour is one '+(mode==='mass'?'landmass':'area')+'.</span>'}
for(const b of ALL)document.getElementById(b).onclick=()=>{mode=b;for(const o of ALL)document.getElementById(o).setAttribute('aria-pressed',o===b);draw()};
c.onmousemove=e=>{const r=c.getBoundingClientRect(),x=Math.floor((e.clientX-r.left)/r.width*255),y=254-Math.floor((e.clientY-r.top)/r.height*255),i=x*255+y;if(i<0||i>=K.length)return;const h=v=>v.toString(16).toUpperCase().padStart(2,'0');document.getElementById('info').textContent=`${h(x)}${h(y)}: ${N[K[i]]}, landmass ${M[i]||'none'}, area ${A[i]||'none'}`+(W.length?`; walks: ${VW[W[i]]}`:'')+(P.length?`; physics: ${VP[P[i]]}`:'')};
if(T.length){document.getElementById('t').innerHTML='<tr><th>Landmass</th><th>km²</th><th>Landblocks</th><th>Areas</th></tr>'+T.slice(0,20).map(m=>`<tr><td>${m.landmass}</td><td>${m.km2}</td><td>${m.landblocks}</td><td>${m.areas}</td></tr>`).join('')}
draw();
</script></body></html>
""";
}
