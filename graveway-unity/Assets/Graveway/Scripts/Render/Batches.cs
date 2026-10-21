// Drawing lots of small things in a handful of draw calls, the way the web
// version uses InstancedMesh: fill in each copy's transform and colour every
// frame, then Draw(). Unity draws at most 1,023 copies per call, so bigger
// batches go out in several.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Graveway
{
    // Copies of one mesh with a colour each (Shaders/Instanced.shader).
    public sealed class InstancedBatch
    {
        const int PerCall = 1023;
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Bounds Everywhere = new Bounds(Vector3.zero, Vector3.one * 100000);

        readonly Mesh mesh;
        readonly Material material;
        readonly Matrix4x4[] matrices;
        readonly Vector4[] colors;
        readonly Matrix4x4[] callMatrices = new Matrix4x4[PerCall];
        readonly Vector4[] callColors = new Vector4[PerCall];
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        public int count;

        public InstancedBatch(Mesh mesh, Material material, int max)
        {
            this.mesh = mesh;
            this.material = material;
            matrices = new Matrix4x4[max];
            colors = new Vector4[max];
        }

        public int Capacity => matrices.Length;

        // `color` is an ordinary (sRGB) colour; the shader works in linear.
        public void Set(int i, Vector3 position, Quaternion rotation, Vector3 scale, Color color)
        {
            matrices[i] = Matrix4x4.TRS(position, rotation, scale);
            colors[i] = color.linear;
        }

        public void Draw()
        {
            for (int start = 0; start < count; start += PerCall)
            {
                int n = Math.Min(PerCall, count - start);
                Array.Copy(matrices, start, callMatrices, 0, n);
                Array.Copy(colors, start, callColors, 0, n);
                block.SetVectorArray(ColorId, callColors);
                var rp = new RenderParams(material)
                {
                    matProps = block, worldBounds = Everywhere,
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                };
                Graphics.RenderMeshInstanced(rp, mesh, 0, callMatrices, n);
            }
        }
    }

    // Camera-facing sprites with a size, spin and tint each (Shaders/Sprite.shader).
    public sealed class SpriteBatch
    {
        const int PerCall = 1023;
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int SizeRotId = Shader.PropertyToID("_SizeRot");
        static readonly Bounds Everywhere = new Bounds(Vector3.zero, Vector3.one * 100000);
        static Mesh quad;

        readonly Material material;
        readonly Matrix4x4[] matrices;
        readonly Vector4[] tints, sizeRots;
        readonly Matrix4x4[] callMatrices = new Matrix4x4[PerCall];
        readonly Vector4[] callTints = new Vector4[PerCall], callSizeRots = new Vector4[PerCall];
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        public int count;

        public SpriteBatch(Material material, int max)
        {
            this.material = material;
            matrices = new Matrix4x4[max];
            tints = new Vector4[max];
            sizeRots = new Vector4[max];
            for (int i = 0; i < max; i++) matrices[i] = Matrix4x4.identity;
        }

        // A 1 x 1 quad in its own xy plane, as three's PlaneGeometry(1, 1).
        static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "sprite quad" };
                quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) });
                quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                quad.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
                quad.RecalculateBounds();
                return quad;
            }
        }

        // tint is linear rgb (the effects' colour maths is done in linear, as
        // the web shader's is) with alpha.
        public void Set(int i, float x, float y, float z, float size, float rot, Vector4 tint)
        {
            matrices[i].m03 = x; matrices[i].m13 = y; matrices[i].m23 = z;
            sizeRots[i] = new Vector4(size, rot, 0, 0);
            tints[i] = tint;
        }

        public void Draw()
        {
            for (int start = 0; start < count; start += PerCall)
            {
                int n = Math.Min(PerCall, count - start);
                Array.Copy(matrices, start, callMatrices, 0, n);
                Array.Copy(tints, start, callTints, 0, n);
                Array.Copy(sizeRots, start, callSizeRots, 0, n);
                block.SetVectorArray(TintId, callTints);
                block.SetVectorArray(SizeRotId, callSizeRots);
                var rp = new RenderParams(material)
                {
                    matProps = block, worldBounds = Everywhere,
                    shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false,
                };
                Graphics.RenderMeshInstanced(rp, Quad, 0, callMatrices, n);
            }
        }
    }
}
