using UnityEngine;

public class RoomBuilder : MonoBehaviour
{
    [Header("Room Size")]
    public float width = 10f;
    public float depth = 8f;
    public float height = 4f;
    public float thickness = 0.2f;

    [Header("Door Opening (front wall)")]
    public float doorWidth = 2f;
    public float doorHeight = 2.5f;

    [Header("Colors")]
    public Color floorColor = new Color(0.12f, 0.12f, 0.12f);
public Color wallColor = new Color(0.38f, 0.36f, 0.34f);
    public Color ceilingColor = new Color(0.18f, 0.18f, 0.18f);

    // Runtime wall appearance.
    // This is intentionally independent from the saved Inspector wallColor
    // so the generated walls cannot become almost black.
    private static readonly Color RuntimeWallColor =
        new Color(0.42f, 0.40f, 0.38f, 1f);

    // Subtle wall-only emission to keep the walls readable in dark areas.
    private static readonly Color WallGlow =
        new Color(0.16f, 0.17f, 0.19f, 1f);

    private Texture2D wallGlowTex;
    private Transform roomRoot;

    void Awake()
    {
        BuildRoom();
    }

    void BuildRoom()
    {
        GameObject root = new GameObject("Room");
        root.transform.SetParent(transform, false);
        roomRoot = root.transform;

        wallGlowTex = new Texture2D(
            1,
            1,
            TextureFormat.RGBA32,
            false
        );

        wallGlowTex.SetPixel(0, 0, WallGlow);
        wallGlowTex.Apply();

        float t = thickness;
        float fullWidth = width + 2f * t;
        float fullDepth = depth + 2f * t;

        // Floor - unchanged.
        CreatePart(
            "Floor",
            new Vector3(0f, -t / 2f, 0f),
            new Vector3(fullWidth, t, fullDepth),
            floorColor
        );

        // Ceiling - unchanged.
        CreatePart(
            "Ceiling",
            new Vector3(0f, height + t / 2f, 0f),
            new Vector3(fullWidth, t, fullDepth),
            ceilingColor
        );

        // Back wall.
        CreatePart(
            "BackWall",
            new Vector3(
                0f,
                height / 2f,
                depth / 2f + t / 2f
            ),
            new Vector3(
                fullWidth,
                height,
                t
            ),
            RuntimeWallColor,
            true
        );

        // Left wall.
        CreatePart(
            "LeftWall",
            new Vector3(
                -(width / 2f + t / 2f),
                height / 2f,
                0f
            ),
            new Vector3(
                t,
                height,
                depth
            ),
            RuntimeWallColor,
            true
        );

        // Right wall.
        CreatePart(
            "RightWall",
            new Vector3(
                width / 2f + t / 2f,
                height / 2f,
                0f
            ),
            new Vector3(
                t,
                height,
                depth
            ),
            RuntimeWallColor,
            true
        );

        // Front wall with door opening.
        float frontZ =
            -(depth / 2f + t / 2f);

        float sideWidth =
            (fullWidth - doorWidth) / 2f;

        float sideCenterX =
            doorWidth / 2f +
            sideWidth / 2f;

        CreatePart(
            "FrontWallLeft",
            new Vector3(
                -sideCenterX,
                height / 2f,
                frontZ
            ),
            new Vector3(
                sideWidth,
                height,
                t
            ),
            RuntimeWallColor,
            true
        );

        CreatePart(
            "FrontWallRight",
            new Vector3(
                sideCenterX,
                height / 2f,
                frontZ
            ),
            new Vector3(
                sideWidth,
                height,
                t
            ),
            RuntimeWallColor,
            true
        );

        float topHeight =
            height - doorHeight;

        CreatePart(
            "FrontWallTop",
            new Vector3(
                0f,
                doorHeight + topHeight / 2f,
                frontZ
            ),
            new Vector3(
                doorWidth,
                topHeight,
                t
            ),
            RuntimeWallColor,
            true
        );

        CreateLights();
    }

    void CreatePart(
        string partName,
        Vector3 localPosition,
        Vector3 scale,
        Color color,
        bool glow = false)
    {
        GameObject part =
            GameObject.CreatePrimitive(
                PrimitiveType.Cube
            );

        part.name = partName;

        part.transform.SetParent(
            roomRoot,
            false
        );

        part.transform.localPosition =
            localPosition;

        part.transform.localScale =
            scale;

        Renderer partRenderer =
            part.GetComponent<Renderer>();

        Material partMaterial =
            partRenderer.material;

        // Apply the requested wall color directly to the generated material.
        partMaterial.color = color;

        if (glow)
        {
            partMaterial.EnableKeyword("_EMISSION");

            partMaterial.SetTexture(
                "_EmissionMap",
                wallGlowTex
            );

            partMaterial.SetColor(
                "_EmissionColor",
                Color.white
            );

            partMaterial.globalIlluminationFlags =
                MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
    }

    void CreateLights()
    {
        GameObject dirObj =
            new GameObject(
                "Directional Light (Room)"
            );

        dirObj.transform.SetParent(
            roomRoot,
            false
        );

        dirObj.transform.rotation =
            Quaternion.Euler(
                50f,
                -30f,
                0f
            );

        Light dirLight =
            dirObj.AddComponent<Light>();

        dirLight.type =
            LightType.Directional;

        dirLight.color =
            new Color(
                0.6f,
                0.7f,
                1f
            );

        dirLight.intensity = 0.15f;
        dirLight.shadows =
            LightShadows.None;

        GameObject pointObj =
            new GameObject(
                "Point Light (Room)"
            );

        pointObj.transform.SetParent(
            roomRoot,
            false
        );

        pointObj.transform.localPosition =
            new Vector3(
                0f,
                height - 0.5f,
                0f
            );

        Light pointLight =
            pointObj.AddComponent<Light>();

        pointLight.type =
            LightType.Point;

        pointLight.color =
            new Color(
                1f,
                0.75f,
                0.45f
            );

        pointLight.intensity = 4f;
        pointLight.range = 9f;
        pointLight.shadows =
            LightShadows.Soft;

        // Keep the original dark ambient atmosphere.
        RenderSettings.ambientMode =
            UnityEngine.Rendering.AmbientMode.Flat;

        RenderSettings.ambientLight =
            new Color(
                0.03f,
                0.03f,
                0.04f
            );
    }
}