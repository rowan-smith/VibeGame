using System;
using System.Collections.Generic;
using System.IO;
using Assimp;
using ZeroElectric.Vinculum;
using Mesh = ZeroElectric.Vinculum.Mesh;
using System.Numerics;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace VibeGame.Core
{
    /// <summary>
    /// Loads GLB/GLTF files via AssimpNet and converts meshes into Raylib Models.
    /// Supports per-mesh models or batching all meshes into a single model.
    /// Preserves node transforms and optionally supports tangents/binormals.
    /// Fixes GLB rotation to Y-up automatically.
    /// </summary>
    public static class RaylibGLBLoader
    {
        private static readonly AssimpContext _importer = new();
        private static readonly Matrix4x4 GlbToRaylibCorrection = Matrix4x4.CreateRotationX(-MathF.PI / 2);

        public static List<Model> LoadGLB(string path, bool batchAllMeshes = false)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path is empty", nameof(path));
            if (!File.Exists(path))
                throw new FileNotFoundException($"GLB/GLTF file not found: {path}", path);

            var postProcess = PostProcessSteps.Triangulate |
                              PostProcessSteps.FlipUVs |
                              PostProcessSteps.GenerateNormals |
                              PostProcessSteps.JoinIdenticalVertices |
                              PostProcessSteps.CalculateTangentSpace;

            var scene = _importer.ImportFile(path, postProcess)
                        ?? throw new Exception($"Failed to load GLB/GLTF or no meshes present: {path}");

            if (batchAllMeshes)
            {
                var allVertices = new List<Vector3>();
                var allNormals = new List<Vector3>();
                var allUVs = new List<Vector2>();
                var allIndices = new List<ushort>();
                ushort indexOffset = 0;

                TraverseNode(scene.RootNode, scene, Matrix4x4.Identity,
                    (pos, normal, uv, indices) =>
                    {
                        allVertices.AddRange(pos);
                        if (normal != null) allNormals.AddRange(normal);
                        if (uv != null) allUVs.AddRange(uv);
                        foreach (var idx in indices)
                            allIndices.Add((ushort)(idx + indexOffset));
                        indexOffset += (ushort)pos.Length;
                    });

                var batchedMesh = CreateMesh(allVertices.ToArray(),
                                             allNormals.Count > 0 ? allNormals.ToArray() : null,
                                             allUVs.Count > 0 ? allUVs.ToArray() : null,
                                             allIndices.ToArray());

                unsafe
                {
                    Mesh* mPtr = &batchedMesh;
                    Raylib.UploadMesh(mPtr, false);
                }

                return new List<Model> { Raylib.LoadModelFromMesh(batchedMesh) };
            }
            else
            {
                var models = new List<Model>();
                TraverseNode(scene.RootNode, scene, models, Matrix4x4.Identity);
                return models;
            }
        }

        private static void TraverseNode(Node node, Scene scene, List<Model> models, Matrix4x4 parentTransform)
        {
            var local = ConvertAssimpMatrix(node.Transform);
            var worldTransform = GlbToRaylibCorrection * local * parentTransform;

            foreach (int meshIndex in node.MeshIndices)
            {
                var aMesh = scene.Meshes[meshIndex];
                var rlMesh = ConvertAssimpMeshToRaylib(aMesh, worldTransform);
                unsafe
                {
                    Mesh* mPtr = &rlMesh;
                    Raylib.UploadMesh(mPtr, false);
                }
                models.Add(Raylib.LoadModelFromMesh(rlMesh));
            }

            foreach (var child in node.Children)
                TraverseNode(child, scene, models, worldTransform);
        }

        private static void TraverseNode(Node node, Scene scene, Matrix4x4 parentTransform,
            Action<Vector3[], Vector3[]?, Vector2[]?, ushort[]> onMesh)
        {
            var local = ConvertAssimpMatrix(node.Transform);
            var worldTransform = GlbToRaylibCorrection * local * parentTransform;

            foreach (int meshIndex in node.MeshIndices)
            {
                var aMesh = scene.Meshes[meshIndex];
                var meshData = ConvertAssimpMesh(aMesh, worldTransform);
                onMesh(meshData.positions, meshData.normals, meshData.uvs, meshData.indices);
            }

            foreach (var child in node.Children)
                TraverseNode(child, scene, worldTransform, onMesh);
        }

        private static (Vector3[] positions, Vector3[]? normals, Vector2[]? uvs, ushort[] indices)
            ConvertAssimpMesh(Assimp.Mesh aMesh, Matrix4x4 transform)
        {
            int vCount = aMesh.VertexCount;
            var positions = new Vector3[vCount];
            var normalsArr = aMesh.Normals != null && aMesh.Normals.Count == vCount ? new Vector3[vCount] : null;
            var texcoords = aMesh.TextureCoordinateChannelCount > 0 ? new Vector2[vCount] : null;

            for (int i = 0; i < vCount; i++)
            {
                var v = aMesh.Vertices[i];
                positions[i] = Vector3.Transform(new Vector3(v.X, v.Y, v.Z), transform);

                if (normalsArr != null)
                {
                    var n = aMesh.Normals[i];
                    normalsArr[i] = Vector3.Normalize(Vector3.TransformNormal(new Vector3(n.X, n.Y, n.Z), transform));
                }

                if (texcoords != null && aMesh.TextureCoordinateChannels[0] != null && i < aMesh.TextureCoordinateChannels[0].Count)
                {
                    var uv = aMesh.TextureCoordinateChannels[0][i];
                    texcoords[i] = new Vector2(uv.X, uv.Y);
                }
            }

            var indices = new ushort[aMesh.FaceCount * 3];
            int k = 0;
            foreach (var face in aMesh.Faces)
            {
                if (face.IndexCount >= 3)
                {
                    indices[k++] = (ushort)face.Indices[0];
                    indices[k++] = (ushort)face.Indices[1];
                    indices[k++] = (ushort)face.Indices[2];
                }
            }

            return (positions, normalsArr, texcoords, indices);
        }

        private static unsafe Mesh ConvertAssimpMeshToRaylib(Assimp.Mesh aMesh, Matrix4x4 transform)
        {
            var meshData = ConvertAssimpMesh(aMesh, transform);

            fixed (Vector3* vPtr = meshData.positions)
            fixed (Vector2* uvPtr = meshData.uvs)
            fixed (Vector3* nPtr = meshData.normals)
            fixed (ushort* iPtr = meshData.indices)
            {
                return new Mesh
                {
                    vertices = (float*)vPtr,
                    texcoords = (float*)uvPtr,
                    normals = meshData.normals != null ? (float*)nPtr : null,
                    indices = iPtr,
                    vertexCount = meshData.positions.Length,
                    triangleCount = meshData.indices.Length / 3
                };
            }
        }

        private static unsafe Mesh CreateMesh(Vector3[] positions, Vector3[]? normals, Vector2[]? uvs, ushort[] indices)
        {
            fixed (Vector3* vPtr = positions)
            fixed (Vector2* uvPtr = uvs)
            fixed (Vector3* nPtr = normals)
            fixed (ushort* iPtr = indices)
            {
                return new Mesh
                {
                    vertices = (float*)vPtr,
                    texcoords = (float*)uvPtr,
                    normals = normals != null ? (float*)nPtr : null,
                    indices = iPtr,
                    vertexCount = positions.Length,
                    triangleCount = indices.Length / 3
                };
            }
        }

        private static Matrix4x4 ConvertAssimpMatrix(Assimp.Matrix4x4 assimp)
        {
            return new Matrix4x4(
                assimp.A1, assimp.B1, assimp.C1, assimp.D1,
                assimp.A2, assimp.B2, assimp.C2, assimp.D2,
                assimp.A3, assimp.B3, assimp.C3, assimp.D3,
                assimp.A4, assimp.B4, assimp.C4, assimp.D4
            );
        }
    }
}
