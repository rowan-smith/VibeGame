using System;
using System.Collections.Generic;
using System.IO;
using Assimp;
using ZeroElectric.Vinculum;

namespace VibeGame.Core
{
    /// <summary>
    /// Loads GLB/GLTF files via AssimpNet and converts meshes into Raylib Models (one mesh per Model).
    /// This enables multi-mesh GLBs to be used as multiple selectable models.
    /// </summary>
    public static class RaylibGLBLoader
    {
        /// <summary>
        /// Load a GLB/GLTF file and return one Raylib Model per mesh found in the scene hierarchy.
        /// Models use default materials (textures not auto-bound). Orientation/scale handled by callers.
        /// </summary>
        public static List<Model> LoadGLB(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is empty", nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException($"GLB/GLTF file not found: {path}", path);

            var importer = new AssimpContext();
            var pp = PostProcessSteps.Triangulate |
                     PostProcessSteps.FlipUVs |
                     PostProcessSteps.GenerateNormals |
                     PostProcessSteps.JoinIdenticalVertices;

            var scene = importer.ImportFile(path, pp);
            if (scene == null || scene.MeshCount == 0)
                throw new Exception($"Failed to load GLB/GLTF or no meshes present: {path}");

            var models = new List<Model>();
            TraverseNode(scene.RootNode, scene, models);
            return models;
        }

        private static void TraverseNode(Node node, Scene scene, List<Model> models)
        {
            // For each mesh referenced by this node, convert and wrap as a single-mesh Model
            foreach (int meshIndex in node.MeshIndices)
            {
                var aMesh = scene.Meshes[meshIndex];
                var rlMesh = ConvertAssimpMeshToRaylib(aMesh);

                unsafe
                {
                    // Upload static buffers
                    ZeroElectric.Vinculum.Mesh* mPtr = &rlMesh;
                    Raylib.UploadMesh(mPtr, false);
                }

                // Create a Model from this Mesh (default material)
                var model = Raylib.LoadModelFromMesh(rlMesh);
                models.Add(model);
            }

            // Recurse children
            foreach (var child in node.Children)
                TraverseNode(child, scene, models);
        }

        /// <summary>
        /// Convert an Assimp mesh to a Raylib Mesh using the same unsafe upload pattern as the terrain code.
        /// </summary>
        private static unsafe ZeroElectric.Vinculum.Mesh ConvertAssimpMeshToRaylib(Assimp.Mesh aMesh)
        {
            int vCount = aMesh.VertexCount;

            // Positions
            var positions = new System.Numerics.Vector3[vCount];
            for (int i = 0; i < vCount; i++)
            {
                var v = aMesh.Vertices[i];
                positions[i] = new System.Numerics.Vector3(v.X, v.Y, v.Z);
            }

            // Normals (optional)
            System.Numerics.Vector3[]? normalsArr = null;
            if (aMesh.Normals != null && aMesh.Normals.Count == vCount)
            {
                normalsArr = new System.Numerics.Vector3[vCount];
                for (int i = 0; i < vCount; i++)
                {
                    var n = aMesh.Normals[i];
                    normalsArr[i] = new System.Numerics.Vector3(n.X, n.Y, n.Z);
                }
            }

            // UVs (first channel only)
            var texcoords = new System.Numerics.Vector2[vCount];
            if (aMesh.TextureCoordinateChannelCount > 0)
            {
                var ch0 = aMesh.TextureCoordinateChannels[0];
                if (ch0 != null)
                {
                    int tCount = ch0.Count < vCount ? ch0.Count : vCount;
                    for (int i = 0; i < tCount; i++)
                    {
                        var uv = ch0[i];
                        texcoords[i] = new System.Numerics.Vector2(uv.X, uv.Y);
                    }
                }
            }

            // Indices (faces are triangles due to Triangulate)
            int triCount = aMesh.FaceCount;
            var indices = new ushort[triCount * 3];
            int k = 0;
            for (int f = 0; f < aMesh.FaceCount; f++)
            {
                var face = aMesh.Faces[f];
                // Safety: only take first 3 indices
                if (face.IndexCount >= 3)
                {
                    indices[k++] = (ushort)face.Indices[0];
                    indices[k++] = (ushort)face.Indices[1];
                    indices[k++] = (ushort)face.Indices[2];
                }
            }

            fixed (System.Numerics.Vector3* vPtr = positions)
            fixed (System.Numerics.Vector2* uvPtr = texcoords)
            fixed (System.Numerics.Vector3* nPtr = normalsArr)
            fixed (ushort* iPtr = indices)
            {
                ZeroElectric.Vinculum.Mesh rlMesh = new ZeroElectric.Vinculum.Mesh
                {
                    vertices = (float*)vPtr,
                    texcoords = (float*)uvPtr,
                    normals = normalsArr != null ? (float*)nPtr : null,
                    indices = iPtr,
                    vertexCount = positions.Length,
                    triangleCount = indices.Length / 3
                };

                return rlMesh;
            }
        }
    }
}
