
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class HorrorMaterialGenerator
{
    public static Texture2D CreateNoiseTexture(int width, int height, Color baseColor, Color stainColor, float scale)
    {
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, true);
        Color[] pixels = new Color[width * height];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float xCoord = (float)x / width * scale;
                float yCoord = (float)y / height * scale;

                float n1 = Mathf.PerlinNoise(xCoord, yCoord);
                float n2 = Mathf.PerlinNoise(xCoord * 3f, yCoord * 3f) * 0.5f;
                float noiseVal = Mathf.Clamp01(n1 + n2);

                float streak = Mathf.PerlinNoise(
                    (float)x / width * 15f,
                    (float)y / height * 2f
                );

                if (streak > 0.65f)
                    noiseVal = Mathf.Lerp(noiseVal, 1f, 0.4f);

                pixels[y * width + x] =
                    Color.Lerp(baseColor, stainColor, noiseVal);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();

        return tex;
    }

    public static Material CreateProceduralMaterial(
        Color baseColor,
        Color stainColor,
        float scale,
        float smoothness,
        float metallic)
    {
        Shader urpShader =
            Shader.Find("Universal Render Pipeline/Lit");

        if (urpShader == null)
            urpShader = Shader.Find("Standard");

        Material mat = new Material(urpShader);

        Texture2D noiseTex =
            CreateNoiseTexture(256, 256, baseColor, stainColor, scale);

        mat.mainTexture = noiseTex;
        mat.SetFloat("_Smoothness", smoothness);

        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", metallic);

        return mat;
    }

    public static void SetupPostProcessing(GameObject parent)
    {
        Volume volume = parent.GetComponent<Volume>();

        if (volume == null)
            volume = parent.AddComponent<Volume>();

        volume.isGlobal = true;

        VolumeProfile profile =
            ScriptableObject.CreateInstance<VolumeProfile>();

        Vignette vignette = profile.Add<Vignette>();

        if (vignette != null)
        {
            vignette.active = true;
            vignette.color.Override(Color.black);

            // Reduced slightly so the walls are more visible.
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.4f);
        }

        FilmGrain grain = profile.Add<FilmGrain>();

        if (grain != null)
        {
            grain.active = true;
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.45f);
            grain.response.Override(0.8f);
        }

        ColorAdjustments colorAdj =
            profile.Add<ColorAdjustments>();

        if (colorAdj != null)
        {
            colorAdj.active = true;

            // Keep the room from becoming too dark.
            colorAdj.postExposure.Override(0f);
            colorAdj.contrast.Override(15f);
            colorAdj.saturation.Override(-25f);

            colorAdj.colorFilter.Override(
                new Color(0.82f, 0.90f, 0.88f)
            );
        }

        volume.profile = profile;
    }
}
