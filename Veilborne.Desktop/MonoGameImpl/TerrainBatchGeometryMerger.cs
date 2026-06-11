using Microsoft.Xna.Framework.Graphics;

namespace Veilborne.MonoGameImpl
{
    /// <summary>
    /// Concatenates per-chunk terrain meshes into one dynamic VB/IB for a single draw call.
    /// </summary>
    internal sealed class TerrainBatchGeometryMerger : IDisposable
    {
        private readonly GraphicsDevice _graphicsDevice;
        private DynamicVertexBuffer? _vertexBuffer;
        private DynamicIndexBuffer? _indexBuffer;
        private int _vertexCapacity;
        private int _indexCapacity;
        private VertexPositionColorTexture[]? _vertexScratch;
        private int[]? _indexScratch;

        public TerrainBatchGeometryMerger(GraphicsDevice graphicsDevice) => _graphicsDevice = graphicsDevice;

        public bool TryPrepareMerged<TChunk>(
            IReadOnlyList<TChunk> batch,
            Func<TChunk, VertexPositionColorTexture[]?> getVertices,
            Func<TChunk, int> getIndexCount,
            Func<TChunk, (int width, int depth)?> getTopology,
            Func<(int width, int depth), int[]> getIndices,
            out int triangleCount)
        {
            triangleCount = 0;
            if (batch.Count <= 1)
                return false;

            int totalVertices = 0;
            int totalIndices = 0;
            for (int i = 0; i < batch.Count; i++)
            {
                var verts = getVertices(batch[i]);
                if (verts == null || verts.Length == 0)
                    return false;
                totalVertices += verts.Length;
                totalIndices += getIndexCount(batch[i]);
            }

            EnsureCapacity(totalVertices, totalIndices);
            if (_vertexScratch == null || _indexScratch == null)
                return false;

            int vertexOffset = 0;
            int indexCursor = 0;
            for (int i = 0; i < batch.Count; i++)
            {
                var verts = getVertices(batch[i])!;
                Array.Copy(verts, 0, _vertexScratch, vertexOffset, verts.Length);

                var topo = getTopology(batch[i]);
                if (topo == null)
                    return false;
                var sourceIndices = getIndices(topo.Value);
                for (int j = 0; j < sourceIndices.Length; j++)
                    _indexScratch[indexCursor++] = sourceIndices[j] + vertexOffset;

                vertexOffset += verts.Length;
            }

            _vertexBuffer!.SetData(_vertexScratch, 0, totalVertices, SetDataOptions.Discard);
            _indexBuffer!.SetData(_indexScratch, 0, totalIndices, SetDataOptions.Discard);
            _graphicsDevice.SetVertexBuffer(_vertexBuffer);
            _graphicsDevice.Indices = _indexBuffer;
            triangleCount = totalIndices / 3;
            return triangleCount > 0;
        }

        private void EnsureCapacity(int vertexCount, int indexCount)
        {
            if (_vertexBuffer == null || vertexCount > _vertexCapacity)
            {
                _vertexCapacity = Math.Max(vertexCount, _vertexCapacity * 2);
                _vertexBuffer?.Dispose();
                _vertexBuffer = new DynamicVertexBuffer(
                    _graphicsDevice,
                    typeof(VertexPositionColorTexture),
                    _vertexCapacity,
                    BufferUsage.WriteOnly);
                _vertexScratch = new VertexPositionColorTexture[_vertexCapacity];
            }

            if (_indexBuffer == null || indexCount > _indexCapacity)
            {
                _indexCapacity = Math.Max(indexCount, Math.Max(256, _indexCapacity * 2));
                _indexBuffer?.Dispose();
                _indexBuffer = new DynamicIndexBuffer(
                    _graphicsDevice,
                    IndexElementSize.ThirtyTwoBits,
                    _indexCapacity,
                    BufferUsage.WriteOnly);
                _indexScratch = new int[_indexCapacity];
            }
        }

        public void Dispose()
        {
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
        }
    }
}
