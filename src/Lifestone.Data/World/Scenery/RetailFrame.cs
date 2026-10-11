// Re-expressed from the retail client's Frame / AC1Legacy::Vector3 behaviour (acclient 11.4186 decompile,
// Mag-nus/acclient-AI-RE, and the matching x87 code in the user's acclient.exe 11.6096). No decompiled code is copied.
// Functional reference: ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/AFrame.cs (AGPL-3.0).

using System.Numerics;

namespace Lifestone.Data.World.Scenery;

/// <summary>
/// The retail <c>Frame</c> (quaternion + cached 3x3 matrix + origin) with exactly the arithmetic retail uses for
/// scenery placement: <c>Frame::cache</c>, <c>Frame::set_rotate</c>, <c>Frame::euler_set_rotate</c> (order 0),
/// <c>Frame::set_vector_heading</c>, <c>Frame::set_heading</c>, <c>Frame::localtoglobal</c> and
/// <c>AC1Legacy::Vector3::get_heading</c>.
/// </summary>
/// <remarks>
/// <para>
/// Precision: the retail client runs x87 at 53-bit precision with round-to-nearest (<c>ClientMain::ConfigureFPU</c>:
/// <c>_control87(_PC_53, _MCW_PC)</c>) and creates its D3D9 device with <c>D3DCREATE_FPU_PRESERVE</c> (behaviour flags
/// <c>|= 0x106</c> in the device-creation loop), so register arithmetic is IEEE double. Values the compiler spills to
/// 4-byte stack slots or stores into float fields are rounded to float; we round at exactly those points (read from the
/// x87 code of 11.6096: Frame::cache 0x535B30, set_rotate 0x535DC0, euler_set_rotate 0x536930, set_vector_heading
/// 0x536AF0, set_heading 0x536B80, localtoglobal 0x452660, Vector3::get_heading 0x4576F0).
/// </para>
/// <para>
/// Heading convention: heading 0 faces +Y (north) and increases clockwise; <c>set_heading(d)</c> is a rotation of -d
/// about +Z. In <c>Vector3::get_heading</c> (0x45771F) and <c>set_vector_heading</c> (0x536B1B) the code loads y and
/// then x before <c>fpatan</c>, so ST1 = y and ST0 = x and it computes <c>atan2(y, x)</c>, which is also what ACE does;
/// Hex-Rays prints the two operands swapped.
/// </para>
/// <para>
/// Matrix layout: <c>M[3 * c + r]</c> is row r, column c of the local-to-global rotation, so M[0..2] is the image of
/// local +X, M[3..5] of local +Y and M[6..8] of local +Z (retail <c>m_fl2gv</c>).
/// </para>
/// </remarks>
internal sealed class RetailFrame
{
    /// <summary>pi / 180 as the retail double constant (0x79C6B0).</summary>
    private const double DegToRad = 0.017453292519943295;

    /// <summary>180 / pi as the retail double constant (0x79C6C8).</summary>
    private const double RadToDeg = 57.29577951308232;

    /// <summary>The float 0.0002 retail uses as its small-vector epsilon (0x79C6AC).</summary>
    private const float SmallEpsilon = 0.0002f;

    private readonly float[] _m = new float[9];

    private RetailFrame()
    {
    }

    public float Qw { get; private set; }

    public float Qx { get; private set; }

    public float Qy { get; private set; }

    public float Qz { get; private set; }

    public Vector3 Origin { get; set; }

    /// <summary>The cached rotation matrix, <c>M[3 * column + row]</c>.</summary>
    public ReadOnlySpan<float> Matrix => _m;

    /// <summary>The orientation as a System.Numerics quaternion (same Hamilton convention).</summary>
    public Quaternion Orientation => new(Qx, Qy, Qz, Qw);

    /// <summary>
    /// A frame as <c>Frame::UnPack</c> builds it: quaternion stored as-is (no normalisation) and the matrix cached.
    /// </summary>
    public static RetailFrame FromStored(Vector3 origin, Quaternion orientation)
    {
        var frame = new RetailFrame
        {
            Origin = origin,
            Qw = orientation.W,
            Qx = orientation.X,
            Qy = orientation.Y,
            Qz = orientation.Z,
        };
        frame.Cache();
        return frame;
    }

    /// <summary>
    /// Retail <c>Frame::IsValid</c> for the quaternion part: no NaN and <c>|norm^2 - 1| &lt;= 0.0002 * 5</c>.
    /// </summary>
    public static bool IsValidQuaternion(Quaternion q)
    {
        if (float.IsNaN(q.W) || float.IsNaN(q.X) || float.IsNaN(q.Y) || float.IsNaN(q.Z))
        {
            return false;
        }

        // Retail sums qz^2 + qy^2 + qx^2 + qw^2 in x87 and stores the result as a float.
        var norm = (float)(((((double)q.Z * q.Z) + ((double)q.Y * q.Y)) + ((double)q.X * q.X)) + ((double)q.W * q.W));
        return !float.IsNaN(norm) && !((double)SmallEpsilon * 5.0 < Math.Abs(norm - 1.0));
    }

    /// <summary>Retail <c>Frame::localtoglobal</c>: origin + R * v, each component rounded to float.</summary>
    public Vector3 LocalToGlobal(Vector3 v)
    {
        var m = _m;
        var x = (((double)m[6] * v.Z) + ((double)m[0] * v.X)) + ((double)m[3] * v.Y) + Origin.X;
        var y = (((double)m[7] * v.Z) + ((double)m[1] * v.X)) + ((double)m[4] * v.Y) + Origin.Y;
        var z = (((double)m[8] * v.Z) + ((double)m[2] * v.X)) + ((double)m[5] * v.Y) + Origin.Z;
        return new Vector3((float)x, (float)y, (float)z);
    }

    /// <summary>
    /// Retail <c>Frame::set_heading(degrees)</c>: keeps the pitch of the current forward (+Y) axis and replaces the
    /// rotation with heading <paramref name="degrees"/>.
    /// </summary>
    public void SetHeading(float degrees)
    {
        var m = _m;
        var rad = degrees * DegToRad;
        var direction = new Vector3(
            (float)Math.Sin(rad),
            (float)Math.Cos(rad),
            (float)((((double)m[8] + m[2]) * 0.0) + m[5]));
        SetVectorHeading(direction);
    }

    /// <summary>Retail <c>Frame::set_vector_heading</c>: Euler (x = pitch from heading.z, z = -heading) rotation.</summary>
    public void SetVectorHeading(Vector3 heading)
    {
        if (!NormalizeCheckSmall(ref heading))
        {
            var z = (float)(-Fmod(450.0 - (Math.Atan2(heading.Y, heading.X) * RadToDeg), 360.0) * DegToRad);
            var x = (float)(Math.Asin(heading.Z) * RadToDeg * DegToRad);
            EulerSetRotateOrder0(x, 0f, z);
        }
    }

    /// <summary>
    /// Retail <c>AC1Legacy::Vector3::get_heading</c> of (x, y): 0 when the horizontal part is shorter than 0.0002, else
    /// <c>fmod(450 - atan2(y, x) * 180 / pi, 360)</c> of the normalised horizontal direction.
    /// </summary>
    public static double GetHeading(Vector3 v)
    {
        var horizontal = new Vector3(v.X, v.Y, 0f);
        if (NormalizeCheckSmall(ref horizontal))
        {
            return 0.0;
        }

        return Fmod(450.0 - (Math.Atan2(horizontal.Y, horizontal.X) * RadToDeg), 360.0);
    }

    /// <summary>
    /// Retail <c>AC1Legacy::Vector3::normalize_check_small</c>: returns true (and leaves <paramref name="v"/> alone)
    /// when its length is below 0.0002, else scales it to unit length with the components rounded to float.
    /// </summary>
    public static bool NormalizeCheckSmall(ref Vector3 v)
    {
        var length = Math.Sqrt((((double)v.X * v.X) + ((double)v.Y * v.Y)) + ((double)v.Z * v.Z));
        if (length < SmallEpsilon)
        {
            return true;
        }

        var inverse = 1.0 / length;
        v = new Vector3((float)(inverse * v.X), (float)(inverse * v.Y), (float)(inverse * v.Z));
        return false;
    }

    /// <summary>
    /// Retail <c>Frame::euler_set_rotate</c> with order 0 (Shoemake EulOrdXYZs: i = X, j = Y, k = Z, no parity flip,
    /// no repetition, static frame), i.e. R = Rz(z) * Ry(y) * Rx(x).
    /// </summary>
    private void EulerSetRotateOrder0(float x, float y, float z)
    {
        var ti = x * 0.5;
        var tj = y * 0.5;
        var th = (float)(z * 0.5); // spilled to a float slot before cos/sin

        var ci = (float)Math.Cos(ti);
        var cj = (float)Math.Cos(tj);
        var ch = (float)Math.Cos(th);
        var si = (float)Math.Sin(ti);
        var sj = Math.Sin(tj); // stays in an x87 register
        var sh = Math.Sin(th); // stays in an x87 register

        var cc = (float)((double)ch * ci);
        var cs = (float)(ci * sh);
        var sc = (float)((double)si * ch);
        var ss = (float)(sh * si);

        var a0 = (float)(((double)sc * cj) - (cs * sj));
        var a1 = (float)((cc * sj) + ((double)ss * cj));
        var a2 = (float)(((double)cs * cj) - (sc * sj));
        var w = (float)((ss * sj) + ((double)cc * cj));
        SetRotate(w, a0, a1, a2);
    }

    /// <summary>Retail <c>Frame::set_rotate</c>: normalise, store as float, re-cache; keep the old rotation if invalid.</summary>
    private void SetRotate(float w, float x, float y, float z)
    {
        var (oldW, oldX, oldY, oldZ) = (Qw, Qx, Qy, Qz);
        var inverse = 1.0 / Math.Sqrt((((((double)w * w) + ((double)x * x)) + ((double)y * y)) + ((double)z * z)));
        Qw = (float)(w * inverse);
        Qx = (float)(x * inverse);
        Qy = (float)(y * inverse);
        Qz = (float)(inverse * z);
        if (IsValidQuaternion(Orientation) && !float.IsNaN(Origin.X) && !float.IsNaN(Origin.Y) && !float.IsNaN(Origin.Z))
        {
            Cache();
        }
        else
        {
            (Qw, Qx, Qy, Qz) = (oldW, oldX, oldY, oldZ);
        }
    }

    /// <summary>C <c>fmod(x, y)</c> (truncated remainder, sign of x); exact, like the CRT <c>_CIfmod</c> retail calls.</summary>
    private static double Fmod(double x, double y) => x % y;

    /// <summary>
    /// Retail <c>Frame::cache</c>: the standard rotation matrix of the (assumed unit) quaternion, evaluated in double
    /// and stored as float.
    /// </summary>
    /// <remarks>
    /// Every doubled product of two float components (2xy, 2wz, ...) is exact in double, so only the sums round.
    /// The diagonal entries subtract their two squares one at a time, <c>(1 - 2y^2) - 2z^2</c>, as retail does.
    /// </remarks>
    private void Cache()
    {
        double w = Qw, x = Qx, y = Qy, z = Qz;
        var twoXX = 2.0 * x * x;
        var twoYY = 2.0 * y * y;
        var twoZZ = 2.0 * z * z;
        var twoXY = 2.0 * x * y;
        var twoXZ = 2.0 * x * z;
        var twoYZ = 2.0 * y * z;
        var twoWX = 2.0 * w * x;
        var twoWY = 2.0 * w * y;
        var twoWZ = 2.0 * w * z;

        // Image of local +X.
        _m[0] = (float)(1.0 - twoYY - twoZZ);
        _m[1] = (float)(twoXY + twoWZ);
        _m[2] = (float)(twoXZ - twoWY);

        // Image of local +Y.
        _m[3] = (float)(twoXY - twoWZ);
        _m[4] = (float)(1.0 - twoXX - twoZZ);
        _m[5] = (float)(twoYZ + twoWX);

        // Image of local +Z.
        _m[6] = (float)(twoXZ + twoWY);
        _m[7] = (float)(twoYZ - twoWX);
        _m[8] = (float)(1.0 - twoXX - twoYY);
    }
}
