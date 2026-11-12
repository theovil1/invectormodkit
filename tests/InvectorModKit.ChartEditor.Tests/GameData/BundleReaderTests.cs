namespace InvectorModKit.ChartEditor.Tests;

public class BundleReaderTests
{
    [Theory]
    [InlineData("basegame_assets_shared.bundle", "assets_shared")]
    [InlineData("ab04f3fa61ea81f730e4beaddac78637_unitybuiltinshaders.bundle", "unitybuiltinshaders")]
    [InlineData("sharedmaterials_assets_all.bundle", "sharedmaterials_assets_all")]
    public void ShortNames(string file, string expected) => Assert.Equal(expected, BundleReader.ShortName(file));

    [Fact]
    public void OpensAFileAnotherProcessHoldsForWriting()
    {
        string path = Path.Combine(Path.GetTempPath(), "imk-shared-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var writer = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                writer.Write(new byte[] { 1, 2, 3 });
                writer.Flush();
                using var reader = BundleReader.OpenShared(path);
                var bytes = new byte[3];
                Assert.Equal(3, reader.Read(bytes, 0, 3));
                Assert.Equal(new byte[] { 1, 2, 3 }, bytes);
            }
        }
        finally { File.Delete(path); }
    }

    [GameFact]
    public void FindsAPickupAndItsShader()
    {
        using var reader = new BundleReader(GameDataStamp.BundlesDir(GameInstall.Dir));
        var root = reader.FindRootGameObject("basegame_assets_shared.bundle", "XBOX Pickup Left X OPTIMIZED");
        Assert.NotNull(root);
        Assert.Equal("assets_shared-" + root.PathId, reader.Id(root));

        var renderer = FindComponent(reader, root, 23); // MeshRenderer, anywhere in the hierarchy
        Assert.NotNull(renderer);
        var materialPtr = reader.Read(renderer)["m_Materials"]["Array"][0];
        var material = reader.Resolve(renderer, materialPtr["m_FileID"].AsInt, materialPtr["m_PathID"].AsLong);
        Assert.NotNull(material);
        var shaderPtr = reader.Read(material)["m_Shader"];
        var shader = reader.Resolve(material, shaderPtr["m_FileID"].AsInt, shaderPtr["m_PathID"].AsLong);
        Assert.NotNull(shader);
        Assert.False(string.IsNullOrEmpty(reader.ShaderName(shader)));
    }

    static AssetRef FindComponent(BundleReader reader, AssetRef gameObject, int typeId)
    {
        AssetRef transform = null;
        foreach (var pair in reader.Read(gameObject)["m_Component"]["Array"].Children)
        {
            var c = reader.Resolve(gameObject, pair["component"]["m_FileID"].AsInt, pair["component"]["m_PathID"].AsLong);
            if (c == null) continue;
            if (c.TypeId == typeId) return c;
            if (c.TypeId == 4 || c.TypeId == 224) transform = c;
        }
        if (transform == null) return null;
        foreach (var child in reader.Read(transform)["m_Children"]["Array"].Children)
        {
            var t = reader.Resolve(transform, child["m_FileID"].AsInt, child["m_PathID"].AsLong);
            var go = reader.Read(t)["m_GameObject"];
            var found = FindComponent(reader, reader.Resolve(t, go["m_FileID"].AsInt, go["m_PathID"].AsLong), typeId);
            if (found != null) return found;
        }
        return null;
    }
}
