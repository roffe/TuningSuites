using System;
using System.Diagnostics;
using SkiaSharp;

namespace MapControls;

/// <summary>
/// GPU renderer for <see cref="Surface3D"/>: txlogger's meshgrid fragment shader ported to SkSL.
/// Every pixel casts an orthographic view ray, walks the grid with a 2D DDA and intersects the two triangles of each visited
/// cell, so the surface costs one rect draw per frame. Values live in a small data image (16 bit in R/G, read as exact texels
/// through a raw image shader) and the camera in uniforms; the palette is a 256x1 image of <see cref="HeatColor"/>.
/// </summary>
/// <remarks>
/// Grid-space conventions, as in txlogger:
///   - one grid unit is one cell (32 model px); vertex (gx, gy) is texel (gx, gy) of the data image
///   - vertex height = z_off + z_gain * value16 in grid units
///   - view = R * ((grid - center) * scale_px) - cam; the viewer sits at +Z looking along -Z, so the nearest surface has
///     the largest view Z
///   - SkSL hands main() the pixel centre in control-local logical px; pix_scale converts to device px so lines and their
///     anti-aliasing stay one device pixel wide whatever the render scaling
/// Differences from the GLSL, forced by SkSL:
///   - discard becomes a transparent return, and the wireframe result is returned premultiplied
///   - loops need constant bounds and SkSL caps a program's unrolled size, so the walk is a constant MAX_STEPS loop with an
///     early break, and its body is kept lean: the solid pass only finds the hit inside the loop (the hit height is the ray's
///     z there, which is what the barycentric blend gave) and colours, lights and outlines it once after the loop. The
///     wireframe is a second effect, the render mode being a compile-time constant of each. A grid whose walk can exceed
///     MAX_STEPS (cols + rows cells) is left to the triangle renderer
///   - corner_height fetches exact texels; cell_ao's half-cell samples, which txlogger takes from a linear sampler, are
///     blended by hand from the four texels around them (the same bilinear result, without filtering in the 16 bit split)
/// </remarks>
internal static class SurfaceShader
{
    private const string Source = """
        const int MAX_STEPS = @STEPS@;
        const bool WIRE = @WIRE@;    // wireframe only, else solid (with grid lines unless render_mode is 1)
        const float BIG = 100000.0;

        uniform shader mesh_tex;     // vertex values, 16 bit in RG
        uniform shader colormap_tex; // 256x1 value -> base colour

        uniform float3x3 rot;        // camera rotation R; view = R * model
        uniform float grid_cols;     // cells in X
        uniform float grid_rows;     // cells in Y
        uniform float scale_px;      // logical px per grid unit
        uniform float height_units;  // full value range height, grid units
        uniform float z_off;         // vertex height = z_off + z_gain * value16
        uniform float z_gain;
        uniform float3 center;       // mesh centre, grid units
        uniform float2 cam;          // camera pan, logical px
        uniform float2 size;         // control size, logical px
        uniform float pix_scale;     // device px per logical px
        uniform float view_zmin;     // view-space depth extent of the mesh, logical px
        uniform float view_zrange;
        uniform float render_mode;   // 0 solid+wire, 1 solid, 2 wireframe
        uniform float3 light;        // light direction in model space, unit length

        float corner_height(float gx, float gy) {
            float4 t = float4(mesh_tex.eval(float2(gx + 0.5, gy + 0.5)));
            float v = floor(t.r * 255.0 + 0.5) * 256.0 + floor(t.g * 255.0 + 0.5);
            return z_off + z_gain * v / 65535.0;
        }

        // height between vertices, bilinear like a linear texture sampler clamped to the grid
        float height_at(float gx, float gy) {
            float x0 = floor(gx);
            float y0 = floor(gy);
            float x1 = min(x0 + 1.0, grid_cols);
            float y1 = min(y0 + 1.0, grid_rows);
            float h0 = mix(corner_height(x0, y0), corner_height(x1, y0), gx - x0);
            float h1 = mix(corner_height(x0, y1), corner_height(x1, y1), gx - x0);
            return mix(h0, h1, gy - y0);
        }

        // narrow the line/AABB overlap [umin, umax] by one slab
        void slab(float o, float d, float lo, float hi, inout float umin, inout float umax) {
            if (abs(d) < 0.00000001) {
                if (o < lo || o > hi) {
                    umin = BIG;
                    umax = -BIG;
                }
                return;
            }
            float t1 = (lo - o) / d;
            float t2 = (hi - o) / d;
            umin = max(umin, min(t1, t2));
            umax = min(umax, max(t1, t2));
        }

        // Moeller-Trumbore; the ray parameter of the hit, BIG on a miss
        float ray_tri(float3 ro, float3 rd, float3 a, float3 b, float3 c) {
            float3 e1 = b - a;
            float3 e2 = c - a;
            float3 pv = cross(rd, e2);
            float det = dot(e1, pv);
            if (abs(det) < 0.0000000001) {
                return BIG;
            }
            float inv_det = 1.0 / det;
            float3 tv = ro - a;
            float u = dot(tv, pv) * inv_det;
            float3 qv = cross(tv, e1);
            float v = dot(rd, qv) * inv_det;
            if (u < -0.0001 || u > 1.0001 || v < -0.0001 || u + v > 1.0001) {
                return BIG;
            }
            return dot(e2, qv) * inv_det;
        }

        // grid point -> (device px x, device px y, view-space z in logical px)
        float3 project_grid(float3 g) {
            float3 v = rot * ((g - center) * scale_px) - float3(cam, 0.0);
            return float3((v.xy + 0.5 * size) * pix_scale, v.z);
        }

        // value colour with depth shading; h in grid units. The wireframe keeps the strong ramp and haze, its only depth
        // cue; on the lit surface the ramp is slight so a colour still reads as the value it shows in the map
        float3 height_color(float h, float view_z) {
            float val = clamp(h / height_units, 0.0, 1.0);
            float3 base = float3(colormap_tex.eval(float2(val * 255.0 + 0.5, 0.5)).rgb);
            float df = clamp((view_z - view_zmin) / view_zrange, 0.0, 1.0);
            float dmin = WIRE ? 0.6 : 0.88;
            float3 rgb = base * (dmin + (1.0 - dmin) * df);
            if (WIRE) {
                rgb.b = min(1.0, rgb.b + (1.0 - df) * 0.05882353);
            }
            // yellow emphasis, faded in around pure yellow so the mid-range stays a continuous gradient
            float yellow = smoothstep(0.6, 0.95, base.r) * smoothstep(0.6, 0.95, base.g) * (1.0 - smoothstep(0.2, 0.4, base.b));
            float boost = 1.0 + 0.1 * yellow;
            rgb.r = min(1.0, rgb.r * boost);
            rgb.g = min(1.0, rgb.g * boost);
            return rgb;
        }

        // closest-point parameter of p on segment a-b
        float seg_param(float2 p, float2 a, float2 b) {
            float2 e = b - a;
            float ee = dot(e, e);
            if (ee < 0.000001) {
                return 0.0;
            }
            return clamp(dot(p - a, e) / ee, 0.0, 1.0);
        }

        // anti-aliased coverage of a line of the given half width at distance d
        float line_mask(float d, float half_w) {
            return 1.0 - smoothstep(half_w - 0.6, half_w + 0.6, d);
        }

        // front-to-back "under" compositing of one projected wireframe segment; ha, hb are its end heights
        void wire_seg(float2 p_dev, float half_w, float3 pa, float3 pb, float ha, float hb, inout float3 acc, inout float acc_a) {
            float h = seg_param(p_dev, pa.xy, pb.xy);
            float mask = line_mask(distance(p_dev, mix(pa.xy, pb.xy, h)), half_w);
            if (mask > 0.0) {
                float3 rgb = height_color(mix(ha, hb, h), mix(pa.z, pb.z, h));
                acc += (1.0 - acc_a) * mask * rgb;
                acc_a += (1.0 - acc_a) * mask;
            }
        }

        // track the nearest cell border for the solid+wireframe grid lines
        void edge_check(float2 p_dev, float3 pa, float3 pb, float ha, float hb, inout float best_d, inout float best_h, inout float best_z) {
            float t = seg_param(p_dev, pa.xy, pb.xy);
            float d = distance(p_dev, mix(pa.xy, pb.xy, t));
            if (d < best_d) {
                best_d = d;
                best_h = mix(ha, hb, t);
                best_z = mix(pa.z, pb.z, t);
            }
        }

        // fake ambient occlusion: darken concave cells from the height-field Laplacian at the cell centre and its four
        // neighbours; convex ridges (negative) are left untouched
        float cell_ao(float cx, float cy) {
            float cxm = max(cx - 0.5, 0.0);
            float cxp = min(cx + 1.5, grid_cols);
            float cym = max(cy - 0.5, 0.0);
            float cyp = min(cy + 1.5, grid_rows);
            float hc = height_at(cx + 0.5, cy + 0.5);
            float lap = height_at(cxp, cy + 0.5) + height_at(cxm, cy + 0.5)
                      + height_at(cx + 0.5, cyp) + height_at(cx + 0.5, cym) - 4.0 * hc;
            float c = clamp(lap / max(height_units, 0.0001), 0.0, 1.0);
            return 1.0 - 0.4 * c;
        }

        // Blinn-Phong with an ambient floor and fake AO. n is the raw triangle normal, which points along -Z for a flat
        // cell, so it is flipped to face up. The underside stays unlit at the ambient level whatever the light does: that
        // darkness is how you tell which side you are looking at while orbiting.
        float3 shade_surface(float3 base, float3 n, float3 view_dir, float ao) {
            float nl = length(n);
            if (nl <= 0.0) {
                return base * ao;
            }
            float3 N = -n / nl;
            if (dot(N, view_dir) < 0.0) {
                return base * (0.32 * ao);
            }
            float diff = max(dot(N, light), 0.0);
            float3 H = normalize(light + view_dir);
            float spec = (diff > 0.0) ? pow(max(dot(N, H), 0.0), 32.0) : 0.0;
            float3 col = base * ((0.32 + 0.68 * diff) * ao);
            col += float3(0.25 * spec);
            return col;
        }

        half4 main(float2 coord) {
            if (coord.x < 0.0 || coord.y < 0.0 || coord.x > size.x || coord.y > size.y) {
                return half4(0.0);
            }
            float2 p_dev = coord * pix_scale;
            float2 view_xy = coord - 0.5 * size + cam;

            // pixel ray in grid space: g(u) = g0 + u * dg with u the view-space depth; g0 = transpose(R) * (view_xy, 0)
            // / scale + center, and a row vector times R is transpose(R) times it
            float3 g0 = float3(view_xy, 0.0) * rot / scale_px + center;
            // towards the camera, unit length since R is orthonormal
            float3 view_dir = float3(0.0, 0.0, 1.0) * rot;
            float3 dg = view_dir / scale_px;

            float z_lo = min(z_off, z_off + z_gain) - 0.05;
            float z_hi = max(z_off, z_off + z_gain) + 0.05;

            float umin = -BIG;
            float umax = BIG;
            slab(g0.x, dg.x, 0.0, grid_cols, umin, umax);
            slab(g0.y, dg.y, 0.0, grid_rows, umin, umax);
            slab(g0.z, dg.z, z_lo, z_hi, umin, umax);
            if (umax <= umin) {
                return half4(0.0);
            }

            // march from the near side (largest view z) toward the far side
            float3 ro = g0 + umax * dg;
            float3 rd = -dg;
            float tend = umax - umin;
            ro += rd * 0.0001;

            float cx = clamp(floor(ro.x), 0.0, grid_cols - 1.0);
            float cy = clamp(floor(ro.y), 0.0, grid_rows - 1.0);

            float step_x = rd.x > 0.0 ? 1.0 : -1.0;
            float step_y = rd.y > 0.0 ? 1.0 : -1.0;
            float td_x = abs(rd.x) < 0.00000001 ? BIG : 1.0 / abs(rd.x);
            float td_y = abs(rd.y) < 0.00000001 ? BIG : 1.0 / abs(rd.y);
            float tm_x = abs(rd.x) < 0.00000001 ? BIG : (rd.x > 0.0 ? cx + 1.0 - ro.x : ro.x - cx) / abs(rd.x);
            float tm_y = abs(rd.y) < 0.00000001 ? BIG : (rd.y > 0.0 ? cy + 1.0 - ro.y : ro.y - cy) / abs(rd.y);

            float half_w = 0.5 * pix_scale;

            float3 acc = float3(0.0);
            float acc_a = 0.0;
            float hit_t = BIG;
            float3 hit_n = float3(0.0, 0.0, -1.0);

            for (int i = 0; i < MAX_STEPS; i++) {
                // cell corners; the fill picks its diagonal per cell, the grid lines are only the cell borders
                float3 A = float3(cx, cy + 1.0, corner_height(cx, cy + 1.0));
                float3 B = float3(cx + 1.0, cy + 1.0, corner_height(cx + 1.0, cy + 1.0));
                float3 C = float3(cx + 1.0, cy, corner_height(cx + 1.0, cy));
                float3 D = float3(cx, cy, corner_height(cx, cy));

                if (WIRE) {
                    float3 pa = project_grid(A);
                    float3 pb = project_grid(B);
                    float3 pc = project_grid(C);
                    float3 pd = project_grid(D);
                    wire_seg(p_dev, half_w, pa, pb, A.z, B.z, acc, acc_a);
                    wire_seg(p_dev, half_w, pb, pc, B.z, C.z, acc, acc_a);
                    wire_seg(p_dev, half_w, pc, pd, C.z, D.z, acc, acc_a);
                    wire_seg(p_dev, half_w, pd, pa, D.z, A.z, acc, acc_a);
                    if (acc_a > 0.995) {
                        break;
                    }
                } else {
                    // fold between the two closest corners so a lone outlier slopes one triangle and the other stays a
                    // plateau (T7Suite's look): ABC + ACD, or ABD + BCD; per-triangle normals keep the plateau flat
                    bool fold_ac = abs(A.z - C.z) <= abs(B.z - D.z);
                    float3 Q = fold_ac ? C : D;
                    float3 P = fold_ac ? A : B;
                    float t1 = ray_tri(ro, rd, A, B, Q);
                    float t2 = ray_tri(ro, rd, P, C, D);
                    if (min(t1, t2) < BIG) {
                        hit_t = min(t1, t2);
                        hit_n = t1 <= t2 ? cross(B - A, Q - A) : cross(C - P, D - P);
                        break;
                    }
                }

                if (min(tm_x, tm_y) >= tend) {
                    break;
                }
                if (tm_x < tm_y) {
                    cx += step_x;
                    tm_x += td_x;
                } else {
                    cy += step_y;
                    tm_y += td_y;
                }
                if (cx < -0.5 || cx > grid_cols - 0.5 || cy < -0.5 || cy > grid_rows - 0.5) {
                    break;
                }
            }

            if (WIRE) {
                // premultiplied: acc already holds colour times coverage
                return acc_a > 0.003 ? half4(half3(acc), acc_a) : half4(0.0);
            }
            if (hit_t >= BIG) {
                return half4(0.0);
            }

            // the loop left cx, cy on the hit cell
            float3 rgb = height_color(ro.z + rd.z * hit_t, umax - hit_t);
            rgb = shade_surface(rgb, hit_n, view_dir, cell_ao(cx, cy));

            if (render_mode < 0.5) {
                float3 A = float3(cx, cy + 1.0, corner_height(cx, cy + 1.0));
                float3 B = float3(cx + 1.0, cy + 1.0, corner_height(cx + 1.0, cy + 1.0));
                float3 C = float3(cx + 1.0, cy, corner_height(cx + 1.0, cy));
                float3 D = float3(cx, cy, corner_height(cx, cy));
                float3 pa = project_grid(A);
                float3 pb = project_grid(B);
                float3 pc = project_grid(C);
                float3 pd = project_grid(D);
                float best_d = BIG;
                float line_h = 0.0;
                float line_z = 0.0;
                edge_check(p_dev, pa, pb, A.z, B.z, best_d, line_h, line_z);
                edge_check(p_dev, pb, pc, B.z, C.z, best_d, line_h, line_z);
                edge_check(p_dev, pc, pd, C.z, D.z, best_d, line_h, line_z);
                edge_check(p_dev, pd, pa, D.z, A.z, best_d, line_h, line_z);
                // only the cell borders, the fold diagonal stays in the cell colour
                float lm = line_mask(best_d, half_w);
                if (lm > 0.0) {
                    rgb = mix(rgb, height_color(line_h, line_z) * 0.45, lm);
                }
            }
            return half4(half3(rgb), 1.0);
        }
        """;

    /// <summary>A compiled effect and the longest DDA walk (cells visited, at most cols + rows) its loop allows.</summary>
    internal sealed record Variant(SKRuntimeEffect Effect, int MaxSteps);

    // SkSL's size cap counts the loop body once per iteration, so the longest loop that compiles is taken: about 224 steps
    // for the solid pass and 80 for the heavier wireframe one with Skia 3.119
    private static readonly int[] s_steps = [256, 224, 192, 160, 128, 112, 96, 80, 64, 48, 32];
    private static readonly Lazy<Variant?> s_solid = new(() => Compile(false));
    private static readonly Lazy<Variant?> s_wire = new(() => Compile(true));

    /// <summary>
    /// The effect for the render mode, or null when SkSL rejected it (logged once per mode); the caller then falls back to
    /// DrawVertices, as it does for a grid too large for the loop.
    /// </summary>
    public static Variant? For(bool wireframe) => (wireframe ? s_wire : s_solid).Value;

    private static Variant? Compile(bool wire)
    {
        string? errors = null;
        foreach (int steps in s_steps)
        {
            string source = Source.Replace("@STEPS@", steps.ToString()).Replace("@WIRE@", wire ? "true" : "false");
            SKRuntimeEffect? effect = SKRuntimeEffect.CreateShader(source, out errors);
            if (effect != null) return new Variant(effect, steps);
        }
        Trace.WriteLine($"Surface3D: SkSL mesh shader ({(wire ? "wireframe" : "solid")}) did not compile, drawing triangles instead: {errors}");
        return null;
    }

    private static readonly Lazy<SKImage> s_offline = new(() => Colormap(k => HeatColor.Interpolate(0, 255, k)));
    private static readonly Lazy<SKImage> s_online = new(() => Colormap(k => HeatColor.Interpolate(0, 255, k, true)));
    // a flat map: HeatColor resolves 0/0 to grey, the fallback draws it that way too
    private static readonly Lazy<SKImage> s_grey = new(() => Colormap(_ => Avalonia.Media.Color.FromRgb(128, 128, 128)));

    public static SKImage ColormapImage(bool online, bool flat) => (flat ? s_grey : online ? s_online : s_offline).Value;

    private static SKImage Colormap(Func<int, Avalonia.Media.Color> color)
    {
        var pixels = new byte[256 * 4];
        for (int k = 0; k < 256; k++)
        {
            Avalonia.Media.Color c = color(k);
            pixels[k * 4] = c.R; pixels[k * 4 + 1] = c.G; pixels[k * 4 + 2] = c.B; pixels[k * 4 + 3] = 255;
        }
        return SKImage.FromPixelCopy(new SKImageInfo(256, 1, SKColorType.Rgba8888, SKAlphaType.Opaque), pixels);
    }

    /// <summary>Vertex values normalised to 16 bit, high byte in R and low in G; texel (gx, gy) is vertex (gx, gy).</summary>
    public static SKImage DataImage(Func<int, int, double> norm, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int gy = 0; gy < height; gy++)
            for (int gx = 0; gx < width; gx++)
            {
                int q = (int)(Math.Clamp(norm(gx, gy), 0, 1) * 65535 + 0.5), o = (gy * width + gx) * 4;
                pixels[o] = (byte)(q >> 8); pixels[o + 1] = (byte)q; pixels[o + 3] = 255;
            }
        return SKImage.FromPixelCopy(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque), pixels);
    }

    /// <summary>The per-frame state, captured on the UI thread.</summary>
    internal sealed class Frame
    {
        public required SKImage Data;
        public required SKImage Colormap;
        public required float[] Rotation;   // column-major float3x3
        public required float GridCols, GridRows, ScalePx, HeightUnits, ZOff, ZGain;
        public required float[] Center, Cam, Size, Light;
        public required float ViewZMin, ViewZRange, RenderMode;
    }

    /// <summary>The surface shader for one draw, in control-local logical px; null if Skia refused the uniforms.</summary>
    public static SKShader? Build(SKRuntimeEffect effect, Frame f, float pixScale)
    {
        using var data = f.Data.ToRawShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Nearest));
        using var colormap = f.Colormap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
        // not SKRuntimeShaderBuilder: disposing that disposes the shared effect too
        using var uniforms = new SKRuntimeEffectUniforms(effect);
        var children = new SKRuntimeEffectChildren(effect);
        children["mesh_tex"] = data;
        children["colormap_tex"] = colormap;
        uniforms["rot"] = f.Rotation;
        uniforms["grid_cols"] = f.GridCols;
        uniforms["grid_rows"] = f.GridRows;
        uniforms["scale_px"] = f.ScalePx;
        uniforms["height_units"] = f.HeightUnits;
        uniforms["z_off"] = f.ZOff;
        uniforms["z_gain"] = f.ZGain;
        uniforms["center"] = f.Center;
        uniforms["cam"] = f.Cam;
        uniforms["size"] = f.Size;
        uniforms["pix_scale"] = pixScale;
        uniforms["view_zmin"] = f.ViewZMin;
        uniforms["view_zrange"] = f.ViewZRange;
        uniforms["render_mode"] = f.RenderMode;
        uniforms["light"] = f.Light;
        return effect.ToShader(uniforms, children);
    }
}
