using NightWorld.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace NightWorld.Tests
{
    public class WrapperPrefabCreatorTests
    {
        [Test]
        public void PrefabPath_MirrorsThirdPartyLayout()
        {
            Assert.AreEqual("Assets/_Project/Prefabs/Pack/FBX/Tree.prefab",
                WrapperPrefabCreator.PrefabPathFor("Assets/ThirdParty/Pack/FBX/Tree.fbx"));
        }

        [Test]
        public void AddColliders_GivesStaticMeshesAMeshCollider()
        {
            var root = ModelWithStaticMesh(out var mesh);

            WrapperPrefabCreator.AddColliders(root);

            Assert.IsNotNull(mesh.GetComponent<MeshCollider>());
            Object.DestroyImmediate(root);
        }

        [Test]
        public void AddColliders_SkipsRiggedModels()
        {
            var root = ModelWithStaticMesh(out var mesh);
            root.AddComponent<SkinnedMeshRenderer>();

            WrapperPrefabCreator.AddColliders(root);

            Assert.IsNull(mesh.GetComponent<Collider>());
            Object.DestroyImmediate(root);
        }

        private static GameObject ModelWithStaticMesh(out GameObject mesh)
        {
            var root = new GameObject("model");
            mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.transform.SetParent(root.transform);
            return root;
        }
    }
}
