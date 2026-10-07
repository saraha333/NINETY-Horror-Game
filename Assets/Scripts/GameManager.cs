using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameManager : MonoBehaviour
{
    private const float TimeLimit = 90f;
    private const float InteractDistance = 3f;
    private const float DoorOpenAngle = 95f;
    private const float DrawerSlide = 0.32f;
    private const float LidOpenAngle = 105f;
    private const float EyeHeight = 1.25f;

    private enum GameState { Menu, Exploring, Puzzle, Escaped, GameOver }
    private enum Kind { Phone, Envelope, Drawer, Box, Key, Door }

    private class Interactable
    {
        public Kind kind;
        public int index;
    }

    private class DrawerData
    {
        public Transform root;
        public Vector3 closedPos;
        public Vector3 openPos;
        public bool isOpen;
    }

    private static readonly string[] ObjectiveTexts =
    {
        "Answer the phone.",
        "Find the envelope.",
        "Find the box.",
        "Solve the sequence.",
        "Take the key.",
        "Escape."
    };

    private static GameManager instance;
    private HorrorUI ui;

    private PlayerController player;
    private Camera mainCam;
    private RoomBuilder room;
    private float refreshTimer;

    private float roomW = 10f;
    private float roomD = 8f;
    private float roomH = 4f;
    private float roomT = 0.2f;
    private float doorW = 2f;
    private float doorH = 2.5f;

    // True when no RoomBuilder walls exist in the scene, so GameManager
    // builds the solid (collidable) room shell itself.
    private bool buildRoomShell;

    // Procedural textures, generated once and reused for all surfaces.
    private Texture2D wallpaperTex;
    private Texture2D wallpaperNormal;
    private Texture2D woodVTex;
    private Texture2D woodVNormal;
    private Texture2D woodUTex;
    private Texture2D woodUNormal;
    private Texture2D plasterTex;
    private Texture2D plasterNormal;
    private Texture2D dotTex;

    // Atmosphere helpers.
    private GameObject postVolumeObj;
    private Light flickerLight;
    private float flickerBase;

    private GameState state = GameState.Menu;
    private float timeRemaining = TimeLimit;
    private bool timerRunning;
    private bool phoneRinging;
    private bool callInProgress;
    private bool callDone;
    private bool envelopeRead;
    private bool puzzleSolved;
    private bool hasKey;
    private int objectiveStage = -1;
    private bool puzzleInputLocked;
    private readonly List<PuzzleSymbol> selection = new List<PuzzleSymbol>();

    private GameObject worldRoot;
    private readonly Dictionary<Collider, Interactable> interactables = new Dictionary<Collider, Interactable>();
    private readonly List<DrawerData> drawers = new List<DrawerData>();
    private Vector3 tablePos;

    private GameObject phoneObj;
    private Transform phoneHandset;
    private Renderer phoneLamp;
    private AudioSource phoneAudio;
    private AudioClip ringClip;

    private Transform doorPivot;
    private float doorAngle;
    private float doorTarget;
    private float doorSpeed = 4f;

    private Transform boxLidPivot;
    private float lidAngle;
    private float lidTarget;
    private Collider boxTrigger;
    private GameObject keyObj;

    private GameObject kidnapperObj;
    private Transform kidnapperModel;
    private GameObject exteriorRoot;

    private Quaternion savedCamRotation;
    private bool camRotationSaved;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<GameManager>() != null) return;

        GameObject go = new GameObject("GameManager");
        go.AddComponent<GameManager>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        ui = GetComponent<HorrorUI>();
        if (ui == null) ui = gameObject.AddComponent<HorrorUI>();

        ui.StartPressed = OnStartPressed;
        ui.PlayAgainPressed = OnPlayAgainPressed;
        ui.SymbolPressed = OnSymbolPressed;
        ui.PuzzleResetPressed = OnPuzzleResetPressed;
        ui.PuzzleClosePressed = ClosePuzzle;
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;

            if (postVolumeObj != null)
                Destroy(postVolumeObj);
        }
    }

    void Start()
    {
        if (instance != this) return;

        RemoveLegacyObjects();
        FindReferences();
        ReadRoomDimensions();

        // RoomBuilder.Awake has already run by now. If it did not create walls
        // (e.g. RoomBuilder is not in the scene), GameManager builds a solid shell.
        buildRoomShell = GameObject.Find("BackWall") == null;

        ConfigureAtmosphere();
        BuildRingClip();
        BuildWorld();
        BuildInteriorLighting();
        EnsureGameplayFloor();
        HideRoomBuilderVisuals();
        RefreshReferences();
        EnterMenu();
    }

    private void RemoveLegacyObjects()
    {
        DoorInteraction[] oldDoors = FindObjectsByType<DoorInteraction>(FindObjectsSortMode.None);
        foreach (DoorInteraction d in oldDoors)
            Destroy(d.gameObject);

        KeyPickup[] oldKeys = FindObjectsByType<KeyPickup>(FindObjectsSortMode.None);
        foreach (KeyPickup k in oldKeys)
            Destroy(k.gameObject);
    }

    private void FindReferences()
    {
        player = FindFirstObjectByType<PlayerController>();
        room = FindFirstObjectByType<RoomBuilder>();
        mainCam = Camera.main;
    }

    private void ReadRoomDimensions()
    {
        if (room == null) return;

        roomW = room.width;
        roomD = room.depth;
        roomH = room.height;
        roomT = room.thickness;
        doorW = room.doorWidth;
        doorH = room.doorHeight;
    }

    // If a RoomBuilder room exists, hide its plain meshes (colliders stay) so it
    // does not overlap the detailed room that GameManager draws.
    private void HideRoomBuilderVisuals()
    {
        if (room == null) return;

        Renderer[] renderers = room.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];

            if (r == null) continue;

            if (r.name == "Floor" || r.name == "Ceiling" || r.name.Contains("Wall"))
                r.enabled = false;
        }
    }

    // ------------------------------------------------------------------
    // PROCEDURAL TEXTURES
    // All textures are tileable (periodic noise), generated once, cached.
    // ------------------------------------------------------------------
    private float Frac(float v)
    {
        return v - Mathf.Floor(v);
    }

    // Tileable noise in 0..1. x and y are 0..1; mx / my are integer
    // frequency multipliers so the result wraps seamlessly.
    private float PNoise(float x, float y, int mx, int my, int octaves, int seed)
    {
        float v = 0f;
        float amp = 0.5f;
        float sum = 0f;
        float tau = Mathf.PI * 2f;

        for (int i = 0; i < octaves; i++)
        {
            int f = 1 << i;

            float ph1 = Hash(seed * 3.1f + i * 1.7f) * tau;
            float ph2 = Hash(seed * 5.3f + i * 2.9f) * tau;

            float fx = f * mx * x;
            float fy = f * my * y;

            float a = Mathf.Sin(tau * fx + ph1 + Mathf.Sin(tau * fy + ph2));
            float b = Mathf.Sin(tau * fy + ph2 + 1.3f * Mathf.Sin(tau * fx + ph1));

            v += (a + b) * 0.5f * amp;
            sum += amp;
            amp *= 0.5f;
        }

        return Mathf.Clamp01(v / sum * 0.5f + 0.5f);
    }

    private Texture2D NormalFromHeight(float[] h, int w, int hgt, float strength)
    {
        Texture2D tex = new Texture2D(w, hgt, TextureFormat.RGBA32, true, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 4;

        Color[] px = new Color[w * hgt];

        for (int y = 0; y < hgt; y++)
        {
            int yd = (y - 1 + hgt) % hgt;
            int yu = (y + 1) % hgt;

            for (int x = 0; x < w; x++)
            {
                int xl = (x - 1 + w) % w;
                int xr = (x + 1) % w;

                float dx = h[y * w + xr] - h[y * w + xl];
                float dy = h[yu * w + x] - h[yd * w + x];

                Vector3 n = new Vector3(-dx * strength, -dy * strength, 1f).normalized;

                px[y * w + x] = new Color(
                    n.x * 0.5f + 0.5f,
                    n.y * 0.5f + 0.5f,
                    n.z * 0.5f + 0.5f,
                    1f
                );
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        return tex;
    }

    private void BuildWallpaper()
    {
        int W = 240;
        int H = 400;

        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 8;

        Color[] px = new Color[W * H];
        float[] height = new float[W * H];

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W;
                float v = y / (float)H;

                // Damask-like lattice: diamond outline, centre dot, offset corner dots.
                float cx = Frac(u * 4f);
                float cy = Frac(v * 5f);
                float dia = Mathf.Abs(cx - 0.5f) + Mathf.Abs(cy - 0.5f);

                float outline = 1f - Mathf.SmoothStep(0f, 0.035f, Mathf.Abs(dia - 0.30f));
                float center = 1f - Mathf.SmoothStep(0.05f, 0.11f, dia);

                float ox = Frac(u * 4f + 0.5f);
                float oy = Frac(v * 5f + 0.5f);
                float dia2 = Mathf.Abs(ox - 0.5f) + Mathf.Abs(oy - 0.5f);
                float dot = 1f - Mathf.SmoothStep(0.03f, 0.075f, dia2);

                float stripe = Mathf.Sin(u * Mathf.PI * 2f * 16f) * 0.5f + 0.5f;
                float motif = Mathf.Clamp01(outline * 0.8f + center + dot * 0.7f);

                float blotch = PNoise(u, v, 1, 1, 3, 3);
                float fine = PNoise(u, v, 16, 24, 2, 7);

                // Grime: darker near the ceiling and a damp band above the chair rail.
                float topStain = Mathf.SmoothStep(0.80f, 1f, v) * (0.35f + 0.65f * blotch);
                float dampBand = (1f - Mathf.SmoothStep(0.26f, 0.42f, v)) * (0.3f + 0.7f * blotch);

                float val = 0.84f
                    + 0.03f * stripe
                    - 0.10f * motif
                    + 0.05f * (fine - 0.5f)
                    - 0.12f * (blotch - 0.5f);

                val *= 1f - 0.38f * topStain - 0.22f * dampBand;

                float stain = Mathf.Clamp01(topStain + dampBand);

                px[y * W + x] = new Color(
                    val,
                    val * (1f - 0.05f * stain),
                    val * (1f - 0.14f * stain),
                    1f
                );

                height[y * W + x] = motif + 0.25f * stripe;
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        wallpaperTex = tex;
        wallpaperNormal = NormalFromHeight(height, W, H, 0.8f);
    }

    private Texture2D GetWallpaperTexture()
    {
        if (wallpaperTex == null) BuildWallpaper();
        return wallpaperTex;
    }

    private Texture2D GetWallpaperNormal()
    {
        if (wallpaperNormal == null) BuildWallpaper();
        return wallpaperNormal;
    }

    private void BuildWood(bool alongU)
    {
        int W = alongU ? 256 : 128;
        int H = alongU ? 128 : 256;

        Texture2D tex = new Texture2D(W, H, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 8;

        Color[] px = new Color[W * H];
        float[] height = new float[W * H];

        int mx = alongU ? 1 : 6;
        int my = alongU ? 6 : 1;

        int fmx = alongU ? 2 : 18;
        int fmy = alongU ? 18 : 2;

        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W;
                float v = y / (float)H;

                float across = alongU ? v : u;

                float warp = PNoise(u, v, mx, my, 3, 11);
                float rings = Mathf.Sin(Mathf.PI * 2f * (across * 5f + warp * 2.2f)) * 0.5f + 0.5f;
                float fiber = PNoise(u, v, fmx, fmy, 2, 23);

                float val = 0.70f + 0.14f * rings + 0.14f * fiber;

                px[y * W + x] = new Color(val, val, val, 1f);
                height[y * W + x] = val;
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        Texture2D normal = NormalFromHeight(height, W, H, 3f);

        if (alongU)
        {
            woodUTex = tex;
            woodUNormal = normal;
        }
        else
        {
            woodVTex = tex;
            woodVNormal = normal;
        }
    }

    private Texture2D GetWoodTexture(bool alongU)
    {
        if (alongU)
        {
            if (woodUTex == null) BuildWood(true);
            return woodUTex;
        }

        if (woodVTex == null) BuildWood(false);
        return woodVTex;
    }

    private Texture2D GetWoodNormal(bool alongU)
    {
        if (alongU)
        {
            if (woodUNormal == null) BuildWood(true);
            return woodUNormal;
        }

        if (woodVNormal == null) BuildWood(false);
        return woodVNormal;
    }

    private void BuildPlaster()
    {
        int S = 128;

        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, true);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.filterMode = FilterMode.Bilinear;
        tex.anisoLevel = 4;

        Color[] px = new Color[S * S];
        float[] height = new float[S * S];

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = x / (float)S;
                float v = y / (float)S;

                float n1 = PNoise(u, v, 2, 2, 3, 5);
                float n2 = PNoise(u, v, 12, 12, 2, 8);
                float stain = Mathf.SmoothStep(0.62f, 0.9f, PNoise(u, v, 1, 1, 3, 9));

                float val = 0.88f + 0.06f * (n1 - 0.5f) + 0.05f * (n2 - 0.5f);
                val *= 1f - 0.25f * stain;

                px[y * S + x] = new Color(
                    val,
                    val * (1f - 0.04f * stain),
                    val * (1f - 0.12f * stain),
                    1f
                );

                height[y * S + x] = n1 * 0.5f + n2 * 0.5f;
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        plasterTex = tex;
        plasterNormal = NormalFromHeight(height, S, S, 1.5f);
    }

    private Texture2D GetPlasterTexture()
    {
        if (plasterTex == null) BuildPlaster();
        return plasterTex;
    }

    private Texture2D GetPlasterNormal()
    {
        if (plasterNormal == null) BuildPlaster();
        return plasterNormal;
    }

    private Texture2D GetSoftDotTexture()
    {
        if (dotTex != null) return dotTex;

        int S = 32;

        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] px = new Color[S * S];

        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f;
                float dy = (y + 0.5f) / S * 2f - 1f;

                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r);
                a *= a;

                px[y * S + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(px);
        tex.Apply();

        dotTex = tex;
        return dotTex;
    }

    // Applies albedo (and optional normal map) with tiling/offset to a primitive.
    // URP Lit uses the _BaseMap scale/offset for the normal and emission maps too.
    private void ApplyMaps(
        GameObject go,
        Texture2D albedo,
        Texture2D normal,
        Vector2 tiling,
        Vector2 offset,
        float bump)
    {
        Renderer r = go.GetComponent<Renderer>();

        if (r == null) return;

        Material m = r.material;

        if (albedo != null)
        {
            if (m.HasProperty("_BaseMap"))
            {
                m.SetTexture("_BaseMap", albedo);
                m.SetTextureScale("_BaseMap", tiling);
                m.SetTextureOffset("_BaseMap", offset);
            }

            if (m.HasProperty("_MainTex"))
            {
                m.SetTexture("_MainTex", albedo);
                m.SetTextureScale("_MainTex", tiling);
                m.SetTextureOffset("_MainTex", offset);
            }
        }

        if (normal != null && m.HasProperty("_BumpMap"))
        {
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_BumpMap", normal);

            if (m.HasProperty("_BumpScale"))
                m.SetFloat("_BumpScale", bump);
        }
    }

    // Roughness / specular response.
    private void Surface(GameObject go, float smoothness, float metallic)
    {
        Renderer r = go.GetComponent<Renderer>();

        if (r == null) return;

        Material m = r.material;

        if (m.HasProperty("_Smoothness"))
            m.SetFloat("_Smoothness", smoothness);

        if (m.HasProperty("_Metallic"))
            m.SetFloat("_Metallic", metallic);
    }

    private void WoodSurface(GameObject go, bool alongU)
    {
        ApplyMaps(
            go,
            GetWoodTexture(alongU),
            GetWoodNormal(alongU),
            Vector2.one,
            Vector2.zero,
            0.6f
        );

        Surface(go, 0.36f, 0f);

        Renderer r = go.GetComponent<Renderer>();

        if (r == null) return;

        Material m = r.material;
        Color c = m.color;

        m.color = new Color(c.r * 1.12f, c.g * 1.12f, c.b * 1.12f, 1f);
    }

    private void NoShadow(GameObject go)
    {
        Renderer r = go.GetComponent<Renderer>();

        if (r != null)
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // ------------------------------------------------------------------
    // ROOM SHELL: wallpaper walls, wood trim, plank floor, rug, ceiling,
    // windows and pictures. Same dimensions and layout as RoomBuilder.
    // ------------------------------------------------------------------
    private void ShellWall(
        Transform parent,
        string partName,
        Vector3 pos,
        Vector3 scale,
        Color color,
        Color glow,
        bool solid,
        Vector2 tiling,
        Vector2 offset)
    {
        GameObject go = Part(
            parent,
            PrimitiveType.Cube,
            partName,
            pos,
            scale,
            color,
            solid
        );

        Glow(go, glow);

        Texture2D albedo = GetWallpaperTexture();

        ApplyMaps(go, albedo, GetWallpaperNormal(), tiling, offset, 0.9f);
        Surface(go, 0.06f, 0f);

        // Let the wallpaper pattern modulate the faint emission instead of a flat fill.
        Material m = go.GetComponent<Renderer>().material;

        if (m.HasProperty("_EmissionMap"))
            m.SetTexture("_EmissionMap", albedo);
    }

    // Wood trim piece with grain texture and satin finish.
    private GameObject Trim(
        Transform parent,
        string partName,
        Vector3 pos,
        Vector3 scale,
        Color color)
    {
        GameObject go = Part(
            parent,
            PrimitiveType.Cube,
            partName,
            pos,
            scale,
            color,
            false
        );

        bool vertical = partName.StartsWith("Wainscot");
        float len = Mathf.Max(scale.x, scale.z);

        Vector2 tiling = vertical
            ? new Vector2(len / 0.6f, 1f)
            : new Vector2(len / 1.5f, 1f);

        ApplyMaps(
            go,
            GetWoodTexture(!vertical),
            GetWoodNormal(!vertical),
            tiling,
            Vector2.zero,
            0.5f
        );

        Surface(go, 0.3f, 0f);

        return go;
    }

    // Horizontal strip that follows the inside of the walls (trim, wainscot,
    // baseboard, crown). When skipDoor is true the front wall is split so the
    // doorway stays open.
    private void WallStrip(
        Transform parent,
        string stripName,
        float y,
        float h,
        float thick,
        Color color,
        bool skipDoor)
    {
        float hw = roomW / 2f;
        float hd = roomD / 2f;

        Trim(parent, stripName + " Back",
            new Vector3(0f, y, hd - thick / 2f),
            new Vector3(roomW, h, thick),
            color);

        Trim(parent, stripName + " Left",
            new Vector3(-hw + thick / 2f, y, 0f),
            new Vector3(thick, h, roomD),
            color);

        Trim(parent, stripName + " Right",
            new Vector3(hw - thick / 2f, y, 0f),
            new Vector3(thick, h, roomD),
            color);

        float fz = -hd + thick / 2f;

        if (!skipDoor)
        {
            Trim(parent, stripName + " Front",
                new Vector3(0f, y, fz),
                new Vector3(roomW, h, thick),
                color);
        }
        else
        {
            float edge = doorW / 2f + 0.08f;
            float len = hw - edge;

            if (len > 0.01f)
            {
                float cx = (hw + edge) / 2f;

                Trim(parent, stripName + " Front L",
                    new Vector3(-cx, y, fz),
                    new Vector3(len, h, thick),
                    color);

                Trim(parent, stripName + " Front R",
                    new Vector3(cx, y, fz),
                    new Vector3(len, h, thick),
                    color);
            }
        }
    }

    // Vertical battens on the wainscot give the panelling real depth.
    private void BuildWainscotBattens(Transform parent)
    {
        float hw = roomW / 2f;
        float hd = roomD / 2f;

        Color batten = new Color(0.17f, 0.10f, 0.06f);

        float y = 0.615f;
        float h = 0.87f;
        float bt = 0.015f;
        float off = 0.02f + bt / 2f;
        float step = 0.62f;

        // Back wall.
        for (float x = -hw + 0.6f; x < hw - 0.4f; x += step)
        {
            Part(parent, PrimitiveType.Cube, "Batten",
                new Vector3(x, y, hd - off),
                new Vector3(0.045f, h, bt),
                batten, false);
        }

        // Side walls.
        for (float z = -hd + 0.6f; z < hd - 0.4f; z += step)
        {
            Part(parent, PrimitiveType.Cube, "Batten",
                new Vector3(-hw + off, y, z),
                new Vector3(bt, h, 0.045f),
                batten, false);

            Part(parent, PrimitiveType.Cube, "Batten",
                new Vector3(hw - off, y, z),
                new Vector3(bt, h, 0.045f),
                batten, false);
        }

        // Front wall, leaving the doorway clear.
        for (float x = -hw + 0.6f; x < hw - 0.4f; x += step)
        {
            if (Mathf.Abs(x) < doorW / 2f + 0.25f) continue;

            Part(parent, PrimitiveType.Cube, "Batten",
                new Vector3(x, y, -hd + off),
                new Vector3(0.045f, h, bt),
                batten, false);
        }
    }

    private void BuildRoomShell()
    {
        Transform p = worldRoot.transform;

        bool solid = buildRoomShell;

        float t = roomT;
        float fullW = roomW + 2f * t;
        float fullD = roomD + 2f * t;
        float tile = 2.4f;

        Color wall = new Color(0.58f, 0.60f, 0.55f);
        Color wallGlow = new Color(0.030f, 0.034f, 0.032f);
        Color ceilC = new Color(0.55f, 0.54f, 0.50f);
        Color ceilGlow = new Color(0.026f, 0.026f, 0.024f);

        Color wainColor = new Color(0.22f, 0.13f, 0.08f);
        Color trimColor = new Color(0.27f, 0.16f, 0.09f);
        Color baseColor = new Color(0.16f, 0.09f, 0.055f);
        Color beamColor = new Color(0.16f, 0.09f, 0.055f);

        // ---------------- Floor: planks with dark seams ----------------
        Part(
            p,
            PrimitiveType.Cube,
            "Shell Floor Base",
            new Vector3(0f, -0.02f - t / 2f, 0f),
            new Vector3(fullW, t, fullD),
            new Color(0.03f, 0.02f, 0.015f),
            false
        );

        int planks = Mathf.CeilToInt(roomW / 0.4f);
        float plankW = roomW / planks;

        for (int i = 0; i < planks; i++)
        {
            float shade = 0.75f + 0.5f * Hash(i * 3.7f + 1.3f);

            Color pc = new Color(
                0.17f * shade * 1.15f,
                0.105f * shade * 1.15f,
                0.065f * shade * 1.15f
            );

            GameObject plank = Part(
                p,
                PrimitiveType.Cube,
                "Shell Plank",
                new Vector3(-roomW / 2f + plankW * (i + 0.5f), -0.05f, 0f),
                new Vector3(plankW - 0.012f, 0.1f, roomD + t),
                pc,
                solid
            );

            Glow(plank, pc * 0.08f);

            ApplyMaps(
                plank,
                GetWoodTexture(false),
                GetWoodNormal(false),
                new Vector2(1f, (roomD + t) / 2.1f),
                new Vector2(0f, Hash(i * 7.1f + 0.4f)),
                0.8f
            );

            // Satin varnish: catches the lamp light without mirroring the sky.
            Surface(plank, 0.42f, 0f);
        }

        // ---------------- Rug (clear of the envelope and nightstand) ----------------
        Vector3 rug = new Vector3(0.7f, 0f, 0.4f);

        Part(p, PrimitiveType.Cube, "Rug Border",
            new Vector3(rug.x, 0.006f, rug.z),
            new Vector3(3.0f, 0.012f, 2.2f),
            new Color(0.20f, 0.05f, 0.05f), false);

        Part(p, PrimitiveType.Cube, "Rug Field",
            new Vector3(rug.x, 0.00675f, rug.z),
            new Vector3(2.7f, 0.0135f, 1.9f),
            new Color(0.30f, 0.08f, 0.07f), false);

        Part(p, PrimitiveType.Cube, "Rug Medallion",
            new Vector3(rug.x, 0.0075f, rug.z),
            new Vector3(1.0f, 0.015f, 1.0f),
            new Vector3(0f, 45f, 0f),
            new Color(0.14f, 0.05f, 0.04f), false);

        Part(p, PrimitiveType.Cube, "Rug Center",
            new Vector3(rug.x, 0.00825f, rug.z),
            new Vector3(0.5f, 0.0165f, 0.5f),
            new Vector3(0f, 45f, 0f),
            new Color(0.34f, 0.25f, 0.15f), false);

        // ---------------- Ceiling ----------------
        GameObject ceiling = Part(
            p,
            PrimitiveType.Cube,
            "Shell Ceiling",
            new Vector3(0f, roomH + t / 2f, 0f),
            new Vector3(fullW, t, fullD),
            ceilC,
            solid
        );

        Glow(ceiling, ceilGlow);

        ApplyMaps(
            ceiling,
            GetPlasterTexture(),
            GetPlasterNormal(),
            new Vector2(fullW / 3f, fullD / 3f),
            Vector2.zero,
            0.5f
        );

        Surface(ceiling, 0.04f, 0f);

        // ---------------- Walls ----------------
        ShellWall(p, "Shell Back Wall",
            new Vector3(0f, roomH / 2f, roomD / 2f + t / 2f),
            new Vector3(fullW, roomH, t),
            wall, wallGlow, solid,
            new Vector2(fullW / tile, 1f), Vector2.zero);

        ShellWall(p, "Shell Left Wall",
            new Vector3(-(roomW / 2f + t / 2f), roomH / 2f, 0f),
            new Vector3(t, roomH, roomD),
            wall, wallGlow, solid,
            new Vector2(roomD / tile, 1f), Vector2.zero);

        ShellWall(p, "Shell Right Wall",
            new Vector3(roomW / 2f + t / 2f, roomH / 2f, 0f),
            new Vector3(t, roomH, roomD),
            wall, wallGlow, solid,
            new Vector2(roomD / tile, 1f), Vector2.zero);

        float frontZ = -(roomD / 2f + t / 2f);
        float sideWidth = (fullW - doorW) / 2f;
        float sideCenterX = doorW / 2f + sideWidth / 2f;

        ShellWall(p, "Shell Front Wall Left",
            new Vector3(-sideCenterX, roomH / 2f, frontZ),
            new Vector3(sideWidth, roomH, t),
            wall, wallGlow, solid,
            new Vector2(sideWidth / tile, 1f), Vector2.zero);

        ShellWall(p, "Shell Front Wall Right",
            new Vector3(sideCenterX, roomH / 2f, frontZ),
            new Vector3(sideWidth, roomH, t),
            wall, wallGlow, solid,
            new Vector2(sideWidth / tile, 1f), Vector2.zero);

        float topHeight = roomH - doorH;

        ShellWall(p, "Shell Front Wall Top",
            new Vector3(0f, doorH + topHeight / 2f, frontZ),
            new Vector3(doorW, topHeight, t),
            wall, wallGlow, solid,
            new Vector2(doorW / tile, topHeight / roomH),
            new Vector2(0f, doorH / roomH));

        // ---------------- Wood paneling and trim ----------------
        WallStrip(p, "Wainscot", 0.525f, 1.05f, 0.02f, wainColor, true);
        WallStrip(p, "Chair Rail", 1.08f, 0.07f, 0.05f, trimColor, true);
        WallStrip(p, "Baseboard", 0.09f, 0.18f, 0.04f, baseColor, true);
        WallStrip(p, "Crown", roomH - 0.07f, 0.14f, 0.07f, trimColor, false);

        BuildWainscotBattens(p);

        // ---------------- Ceiling beams ----------------
        float[] beamZ = { -2.3f, 2.6f };

        for (int i = 0; i < beamZ.Length; i++)
        {
            GameObject beam = Part(p, PrimitiveType.Cube, "Ceiling Beam",
                new Vector3(0f, roomH - 0.09f, beamZ[i]),
                new Vector3(roomW - 0.02f, 0.18f, 0.24f),
                beamColor, false);

            ApplyMaps(
                beam,
                GetWoodTexture(true),
                GetWoodNormal(true),
                new Vector2(roomW / 1.5f, 1f),
                Vector2.zero,
                0.5f
            );

            Surface(beam, 0.25f, 0f);
        }

        // ---------------- Windows and pictures ----------------
        BuildWindow(p, new Vector3(roomW / 2f, 1.85f, 1.6f), new Vector3(-1f, 0f, 0f));
        BuildWindow(p, new Vector3(-roomW / 2f, 1.85f, -1.4f), new Vector3(1f, 0f, 0f));

        BuildPictureFrame(p, new Vector3(roomW * 0.26f, 1.75f, roomD / 2f),
            new Vector3(0f, 0f, -1f), 0.6f, 0.8f, new Color(0.34f, 0.30f, 0.24f));

        BuildPictureFrame(p, new Vector3(0.3f, 1.7f, roomD / 2f),
            new Vector3(0f, 0f, -1f), 0.8f, 0.6f, new Color(0.26f, 0.30f, 0.28f));

        BuildPictureFrame(p, new Vector3(-roomW / 2f, 1.7f, 0.5f),
            new Vector3(1f, 0f, 0f), 0.7f, 0.9f, new Color(0.32f, 0.27f, 0.22f));

        BuildPictureFrame(p, new Vector3(3.2f, 1.7f, -roomD / 2f),
            new Vector3(0f, 0f, 1f), 0.6f, 0.75f, new Color(0.30f, 0.30f, 0.26f));

        Physics.SyncTransforms();
    }

    private void BuildWindow(Transform parent, Vector3 wallPoint, Vector3 inward)
    {
        Color wood = new Color(0.20f, 0.12f, 0.07f);
        Color glass = new Color(0.05f, 0.08f, 0.13f);
        Color curtain = new Color(0.38f, 0.12f, 0.10f);

        GameObject root = new GameObject("Window");
        root.transform.SetParent(parent, false);
        root.transform.position = wallPoint;
        root.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);

        Transform w = root.transform;

        GameObject pane = Part(w, PrimitiveType.Cube, "Glass",
            new Vector3(0f, 0f, 0.01f),
            new Vector3(1.2f, 1.2f, 0.02f),
            glass, false);

        // Cold moonlit night behind the glass.
        Glow(pane, new Color(0.10f, 0.15f, 0.26f));

        Part(w, PrimitiveType.Cube, "Frame Top",
            new Vector3(0f, 0.62f, 0.03f),
            new Vector3(1.34f, 0.08f, 0.06f), wood, false);

        Part(w, PrimitiveType.Cube, "Frame Bottom",
            new Vector3(0f, -0.62f, 0.03f),
            new Vector3(1.34f, 0.08f, 0.06f), wood, false);

        Part(w, PrimitiveType.Cube, "Frame Left",
            new Vector3(-0.63f, 0f, 0.03f),
            new Vector3(0.08f, 1.32f, 0.06f), wood, false);

        Part(w, PrimitiveType.Cube, "Frame Right",
            new Vector3(0.63f, 0f, 0.03f),
            new Vector3(0.08f, 1.32f, 0.06f), wood, false);

        Part(w, PrimitiveType.Cube, "Bar V",
            new Vector3(0f, 0f, 0.03f),
            new Vector3(0.04f, 1.2f, 0.04f), wood, false);

        Part(w, PrimitiveType.Cube, "Bar H",
            new Vector3(0f, 0f, 0.03f),
            new Vector3(1.2f, 0.04f, 0.04f), wood, false);

        Part(w, PrimitiveType.Cube, "Sill",
            new Vector3(0f, -0.68f, 0.07f),
            new Vector3(1.5f, 0.05f, 0.14f), wood, false);

        // Curtains made of three folds per side, alternating depth and shade.
        for (int side = -1; side <= 1; side += 2)
        {
            for (int k = 0; k < 3; k++)
            {
                float shade = 0.85f + 0.3f * Hash(side * 3f + k * 1.9f + 0.5f);
                float zOff = (k % 2 == 0) ? 0.07f : 0.10f;

                Color fold = new Color(
                    curtain.r * shade,
                    curtain.g * shade,
                    curtain.b * shade,
                    1f
                );

                Part(w, PrimitiveType.Cube,
                    side < 0 ? "Curtain L" : "Curtain R",
                    new Vector3(side * (0.45f + k * 0.11f), 0.05f, zOff),
                    new Vector3(0.11f, 1.5f, 0.07f),
                    fold, false);
            }
        }
    }

    private void BuildPictureFrame(
        Transform parent,
        Vector3 wallPoint,
        Vector3 inward,
        float w,
        float h,
        Color canvas)
    {
        Color wood = new Color(0.20f, 0.12f, 0.07f);

        GameObject root = new GameObject("Picture Frame");
        root.transform.SetParent(parent, false);
        root.transform.position = wallPoint;
        root.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);

        Transform f = root.transform;

        Part(f, PrimitiveType.Cube, "Frame",
            new Vector3(0f, 0f, 0.02f),
            new Vector3(w, h, 0.04f), wood, false);

        GameObject cv = Part(f, PrimitiveType.Cube, "Canvas",
            new Vector3(0f, 0f, 0.045f),
            new Vector3(w - 0.1f, h - 0.1f, 0.02f), canvas, false);

        Glow(cv, canvas * 0.12f);

        Part(f, PrimitiveType.Cube, "Figure",
            new Vector3(0f, h * 0.05f, 0.058f),
            new Vector3(w * 0.35f, h * 0.5f, 0.01f),
            canvas * 0.35f, false);
    }

    private void BuildChandelier(Transform parent, Vector3 pos)
    {
        Color metal = new Color(0.12f, 0.08f, 0.05f);
        Color bulbC = new Color(1f, 0.86f, 0.58f);

        GameObject root = new GameObject("Chandelier");
        root.transform.SetParent(parent, false);
        root.transform.position = pos;

        Transform c = root.transform;

        NoShadow(Part(c, PrimitiveType.Cylinder, "Rosette",
            new Vector3(0f, 0.435f, 0f),
            new Vector3(0.55f, 0.012f, 0.55f), new Color(0.22f, 0.20f, 0.17f), false));

        NoShadow(Part(c, PrimitiveType.Cylinder, "Canopy",
            new Vector3(0f, 0.42f, 0f),
            new Vector3(0.3f, 0.02f, 0.3f), metal, false));

        NoShadow(Part(c, PrimitiveType.Cylinder, "Rod",
            new Vector3(0f, 0.2f, 0f),
            new Vector3(0.03f, 0.2f, 0.03f), metal, false));

        NoShadow(Part(c, PrimitiveType.Sphere, "Hub",
            new Vector3(0f, -0.02f, 0f),
            new Vector3(0.16f, 0.12f, 0.16f), metal, false));

        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI * 2f / 6f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));

            NoShadow(Part(c, PrimitiveType.Cube, "Arm",
                dir * 0.15f + new Vector3(0f, -0.03f, 0f),
                new Vector3(0.3f, 0.015f, 0.015f),
                new Vector3(0f, a * Mathf.Rad2Deg, 0f),
                metal, false));

            GameObject bulb = Part(c, PrimitiveType.Sphere, "Bulb",
                dir * 0.3f,
                new Vector3(0.07f, 0.09f, 0.07f), bulbC, false);

            Glow(bulb, bulbC * 2f);
            NoShadow(bulb);
        }
    }

    private void BuildFloorLamp(Transform parent, float x, float z)
    {
        Color metal = new Color(0.08f, 0.06f, 0.045f);
        Color shadeC = new Color(0.62f, 0.55f, 0.40f);

        GameObject root = new GameObject("Floor Lamp");
        root.transform.SetParent(parent, false);
        root.transform.position = new Vector3(x, 0f, z);

        Transform l = root.transform;

        NoShadow(Part(l, PrimitiveType.Cylinder, "Base",
            new Vector3(0f, 0.02f, 0f),
            new Vector3(0.32f, 0.02f, 0.32f), metal, false));

        NoShadow(Part(l, PrimitiveType.Cylinder, "Pole",
            new Vector3(0f, 0.8f, 0f),
            new Vector3(0.04f, 0.8f, 0.04f), metal, false));

        GameObject shade = Part(l, PrimitiveType.Cylinder, "Shade",
            new Vector3(0f, 1.55f, 0f),
            new Vector3(0.38f, 0.14f, 0.38f), shadeC, false);

        Glow(shade, new Color(0.9f, 0.7f, 0.4f) * 0.7f);
        NoShadow(shade);

        GameObject lightObj = new GameObject("Lamp Light");
        lightObj.transform.SetParent(l, false);
        lightObj.transform.localPosition = new Vector3(0f, 1.5f, 0f);

        Light light = lightObj.AddComponent<Light>();

        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.5f);
        light.intensity = 1.6f;
        light.range = 5.5f;
        light.shadows = LightShadows.None;
    }

    private void ConfigureAtmosphere()
    {
        if (mainCam != null)
        {
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = new Color(0.008f, 0.012f, 0.02f);
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.012f, 0.017f, 0.025f);
        RenderSettings.fogDensity = 0.012f;

        // The room should not mirror the default daytime sky on its polished surfaces.
        RenderSettings.reflectionIntensity = 0.25f;

        BuildPostProcessing();
    }

    // Global URP volume: vignette, soft bloom, light grade and grain.
    private void BuildPostProcessing()
    {
        if (postVolumeObj != null) return;

        postVolumeObj = new GameObject("NINETY Post Processing");

        UnityEngine.Rendering.Volume vol =
            postVolumeObj.AddComponent<UnityEngine.Rendering.Volume>();

        vol.isGlobal = true;
        vol.priority = 50f;

        UnityEngine.Rendering.VolumeProfile profile =
            ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();

        UnityEngine.Rendering.Universal.Vignette vignette =
            profile.Add<UnityEngine.Rendering.Universal.Vignette>(true);
        vignette.intensity.Override(0.36f);
        vignette.smoothness.Override(0.45f);

        UnityEngine.Rendering.Universal.Bloom bloom =
            profile.Add<UnityEngine.Rendering.Universal.Bloom>(true);
        bloom.threshold.Override(0.9f);
        bloom.intensity.Override(0.35f);
        bloom.scatter.Override(0.6f);

        UnityEngine.Rendering.Universal.ColorAdjustments grade =
            profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
        grade.postExposure.Override(0.15f);
        grade.contrast.Override(12f);
        grade.saturation.Override(-10f);
        grade.colorFilter.Override(new Color(1f, 0.97f, 0.93f));

        UnityEngine.Rendering.Universal.FilmGrain grain =
            profile.Add<UnityEngine.Rendering.Universal.FilmGrain>(true);
        grain.intensity.Override(0.16f);
        grain.response.Override(0.8f);

        vol.sharedProfile = profile;

        if (mainCam != null)
        {
            UnityEngine.Rendering.Universal.UniversalAdditionalCameraData camData =
                UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(mainCam);

            if (camData != null)
                camData.renderPostProcessing = true;
        }
    }

    void Update()
    {
        if (instance != this) return;

        RefreshReferences();
        UpdateCursorState();
        UpdateTimer();
        UpdateInteraction();
        UpdatePuzzleKeys();
        UpdateAnimations();
        UpdateAtmosphere();
    }

    // Very subtle flicker on one ceiling light, with an occasional brief dip.
    private void UpdateAtmosphere()
    {
        if (flickerLight == null) return;

        float n = Mathf.PerlinNoise(Time.time * 2.3f, 11.7f);

        float dip = 0f;

        if (Mathf.PerlinNoise(Time.time * 0.35f, 3.3f) > 0.86f)
            dip = Mathf.PerlinNoise(Time.time * 24f, 5.1f) * 0.35f;

        flickerLight.intensity = flickerBase * (0.93f + 0.07f * n - dip);
    }

    private void RefreshReferences()
    {
        refreshTimer -= Time.deltaTime;
        if (refreshTimer > 0f) return;

        refreshTimer = 0.5f;

        if (player == null)
            player = FindFirstObjectByType<PlayerController>();

        if (mainCam == null)
            mainCam = Camera.main;
    }

    private void UpdateCursorState()
    {
        if (state == GameState.Exploring)
        {
            if (Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            return;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void UpdateTimer()
    {
        if (!timerRunning) return;
        if (state != GameState.Exploring && state != GameState.Puzzle) return;

        timeRemaining -= Time.deltaTime;

        if (timeRemaining <= 0f)
        {
            timeRemaining = 0f;
            ui.SetTimer(0f, TimeLimit);
            TriggerGameOver();
            return;
        }

        ui.SetTimer(timeRemaining, TimeLimit);
    }

    private void EnterMenu()
    {
        state = GameState.Menu;
        SetPlayerActive(false);
        ui.ShowMainMenu();
    }

    private void OnStartPressed()
    {
        if (state != GameState.Menu) return;

        ui.HideMainMenu();
        BeginRun();
    }

    private void BeginRun()
    {
        StopAllCoroutines();
        ResetRunState();

        state = GameState.Exploring;

        ui.ResetForNewRun();
        ui.ShowHud(true);
        ui.ShowTimer(false);
        SetObjectiveStage(0, true);

        ResetPlayerPosition();
        SetPlayerActive(true);
        LockCursor();

        StartCoroutine(RingRoutine());
    }

    private void ResetRunState()
    {
        timeRemaining = TimeLimit;
        timerRunning = false;
        phoneRinging = false;
        callInProgress = false;
        callDone = false;
        envelopeRead = false;
        puzzleSolved = false;
        hasKey = false;
        puzzleInputLocked = false;
        objectiveStage = -1;
        selection.Clear();
    }

    private void OnPlayAgainPressed()
    {
        if (state != GameState.Escaped && state != GameState.GameOver) return;

        StopAllCoroutines();

        if (mainCam != null && camRotationSaved)
            mainCam.transform.localRotation = savedCamRotation;

        camRotationSaved = false;

        RebuildWorld();
        ResetPlayerPosition();
        BeginRun();
        ui.FadeFromBlack(1f);
    }

    private void SetObjectiveStage(int stage, bool force)
    {
        if (!force && stage <= objectiveStage) return;

        objectiveStage = stage;
        ui.SetObjective(ObjectiveTexts[stage]);
    }

    private IEnumerator RingRoutine()
    {
        yield return new WaitForSeconds(1.5f);

        phoneRinging = true;

        if (phoneAudio != null)
            phoneAudio.Play();

        ui.ShowNotice("THE PHONE IS RINGING", 3f);
    }

    private IEnumerator PhoneCallRoutine()
    {
        callInProgress = true;
        phoneRinging = false;

        StopRing();

        ui.ShowPhoneCall("UNKNOWN VOICE", "Find the envelope.");

        yield return new WaitForSeconds(3.4f);

        ui.HidePhoneCall();

        callInProgress = false;
        callDone = true;

        timeRemaining = TimeLimit;
        timerRunning = true;

        ui.ShowTimer(true);
        ui.SetTimer(timeRemaining, TimeLimit);

        SetObjectiveStage(1, false);
    }

    private void TriggerEscape()
    {
        if (state == GameState.Escaped || state == GameState.GameOver) return;

        state = GameState.Escaped;
        timerRunning = false;

        StopAllCoroutines();

        SetPlayerActive(false);

        ui.SetPrompt(null);
        ui.HidePuzzle();
        ui.HideEnvelopeClue();
        ui.HidePhoneCall();

        if (exteriorRoot != null)
            exteriorRoot.SetActive(true);

        doorTarget = DoorOpenAngle;
        doorSpeed = 4f;

        ui.ShowEnding(true, "You found the key and escaped before time ran out.");
    }

    private void TriggerGameOver()
    {
        if (state == GameState.Escaped || state == GameState.GameOver) return;

        state = GameState.GameOver;
        timerRunning = false;

        StopAllCoroutines();

        SetPlayerActive(false);

        phoneRinging = false;
        StopRing();

        ui.SetPrompt(null);
        ui.HidePuzzle();
        ui.HideEnvelopeClue();
        ui.HidePhoneCall();

        if (exteriorRoot != null)
            exteriorRoot.SetActive(true);

        doorTarget = DoorOpenAngle;
        doorSpeed = 9f;

        float doorwayZ = -(roomD / 2f + roomT / 2f);

        if (kidnapperObj != null)
        {
            kidnapperObj.transform.position = new Vector3(0f, 0f, doorwayZ);
            kidnapperObj.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            kidnapperObj.SetActive(true);
        }

        if (mainCam != null)
        {
            savedCamRotation = mainCam.transform.localRotation;
            camRotationSaved = true;
        }

        StartCoroutine(TurnPlayerToward(new Vector3(0f, 1.6f, doorwayZ), 0.7f));

        ui.ShowEnding(false, "The kidnapper found you.");
    }

    private IEnumerator TurnPlayerToward(Vector3 target, float duration)
    {
        if (player == null || mainCam == null) yield break;

        Vector3 dir = target - player.transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.01f) yield break;

        Quaternion startYaw = player.transform.rotation;
        Quaternion endYaw = Quaternion.LookRotation(dir);

        Quaternion startPitch = mainCam.transform.localRotation;
        Quaternion endPitch = Quaternion.identity;

        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime / duration;

            float s = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));

            player.transform.rotation = Quaternion.Slerp(startYaw, endYaw, s);
            mainCam.transform.localRotation = Quaternion.Slerp(startPitch, endPitch, s);

            yield return null;
        }
    }

    private void SetPlayerActive(bool active)
    {
        if (player == null) return;

        if (player.enabled != active)
            player.enabled = active;
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void ResetPlayerPosition()
    {
        if (player == null) return;

        CharacterController cc = player.GetComponent<CharacterController>();

        if (cc != null)
            cc.enabled = false;

        // Correct floor position.
        // The CharacterController bottom sits exactly on Y = 0.
        player.transform.position = new Vector3(0f, 0f, -2f);
        player.transform.rotation = Quaternion.identity;

        // Lower the actual runtime camera to standing eye level (world space,
        // so it is independent of any serialized camera height or parent scale).
        if (mainCam == null)
            mainCam = Camera.main;

        if (mainCam != null)
        {
            mainCam.transform.position = new Vector3(
                player.transform.position.x,
                player.transform.position.y + EyeHeight,
                player.transform.position.z
            );
        }

        if (cc != null)
            cc.enabled = true;

        Physics.SyncTransforms();
    }

    private void UpdateInteraction()
    {
        if (state != GameState.Exploring) return;

        Interactable target = null;

        if (mainCam != null)
        {
            Ray ray = new Ray(mainCam.transform.position, mainCam.transform.forward);

            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                InteractDistance,
                ~0,
                QueryTriggerInteraction.Collide
            );

            float nearest = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                Interactable candidate;

                if (!interactables.TryGetValue(hits[i].collider, out candidate))
                    continue;

                if (hits[i].distance < nearest)
                {
                    nearest = hits[i].distance;
                    target = candidate;
                }
            }
        }

        string prompt = target != null ? PromptFor(target) : null;

        ui.SetPrompt(prompt);

        Keyboard keyboard = Keyboard.current;

        if (target != null &&
            prompt != null &&
            keyboard != null &&
            keyboard.eKey.wasPressedThisFrame)
        {
            Interact(target);
        }
    }

    private string PromptFor(Interactable it)
    {
        switch (it.kind)
        {
            case Kind.Phone:
                return (phoneRinging && !callInProgress) ? "ANSWER PHONE" : null;

            case Kind.Envelope:
                if (!callDone) return null;
                return envelopeRead ? "READ NOTE" : "OPEN ENVELOPE";

            case Kind.Drawer:
                if (!callDone) return null;
                return drawers[it.index].isOpen ? "CLOSE DRAWER" : "OPEN DRAWER";

            case Kind.Box:
                return (callDone && !puzzleSolved) ? "EXAMINE BOX" : null;

            case Kind.Key:
                return (callDone && puzzleSolved && !hasKey) ? "TAKE KEY" : null;

            case Kind.Door:
                if (!callDone) return null;
                return hasKey ? "OPEN DOOR" : "TRY DOOR";
        }

        return null;
    }

    private void Interact(Interactable it)
    {
        switch (it.kind)
        {
            case Kind.Phone:
                StartCoroutine(PhoneCallRoutine());
                break;

            case Kind.Envelope:
                envelopeRead = true;
                ui.ShowEnvelopeClue(8f);
                SetObjectiveStage(2, false);
                break;

            case Kind.Drawer:
                drawers[it.index].isOpen = !drawers[it.index].isOpen;
                break;

            case Kind.Box:
                OpenPuzzle();
                break;

            case Kind.Key:
                hasKey = true;

                if (keyObj != null)
                    keyObj.SetActive(false);

                ui.ShowNotice("KEY TAKEN", 2.2f);
                SetObjectiveStage(5, false);
                break;

            case Kind.Door:
                if (hasKey)
                    TriggerEscape();
                else
                    ui.ShowNotice("THE DOOR IS LOCKED.", 2.2f);
                break;
        }
    }

    private void OpenPuzzle()
    {
        state = GameState.Puzzle;
        puzzleInputLocked = false;
        selection.Clear();

        SetPlayerActive(false);
        ui.SetPrompt(null);
        ui.ShowPuzzle();

        SetObjectiveStage(3, false);
    }

    private void ClosePuzzle()
    {
        if (state != GameState.Puzzle) return;
        if (puzzleSolved) return;

        puzzleInputLocked = false;

        ui.HidePuzzle();

        state = GameState.Exploring;

        SetPlayerActive(true);
        LockCursor();
    }

    private void UpdatePuzzleKeys()
    {
        if (state != GameState.Puzzle) return;

        Keyboard keyboard = Keyboard.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            ClosePuzzle();
    }

    private void OnSymbolPressed(PuzzleSymbol symbol)
    {
        if (state != GameState.Puzzle || puzzleInputLocked || puzzleSolved)
            return;

        if (selection.Contains(symbol))
            return;

        selection.Add(symbol);
        ui.SetPuzzleSelection(selection);

        if (selection.Count < 3)
            return;

        bool correct =
            selection[0] == PuzzleSymbol.Key &&
            selection[1] == PuzzleSymbol.Box &&
            selection[2] == PuzzleSymbol.Door;

        puzzleInputLocked = true;

        if (correct)
        {
            puzzleSolved = true;
            ui.ShowPuzzleSuccess();
            StartCoroutine(PuzzleSolvedRoutine());
        }
        else
        {
            ui.ShowPuzzleError();
            StartCoroutine(PuzzleRetryRoutine());
        }
    }

    private void OnPuzzleResetPressed()
    {
        if (state != GameState.Puzzle || puzzleInputLocked || puzzleSolved)
            return;

        selection.Clear();
        ui.SetPuzzleSelection(selection);
    }

    private IEnumerator PuzzleRetryRoutine()
    {
        yield return new WaitForSeconds(1f);

        selection.Clear();
        ui.SetPuzzleSelection(selection);
        ui.ClearPuzzleMessage();

        puzzleInputLocked = false;
    }

    private IEnumerator PuzzleSolvedRoutine()
    {
        yield return new WaitForSeconds(1.2f);

        ui.ClearPuzzleMessage();
        ui.HidePuzzle();

        puzzleInputLocked = false;
        state = GameState.Exploring;

        SetPlayerActive(true);
        LockCursor();

        lidTarget = LidOpenAngle;

        if (boxTrigger != null)
            boxTrigger.enabled = false;

        if (keyObj != null)
            keyObj.SetActive(true);

        SetObjectiveStage(4, false);
        ui.ShowNotice("THE BOX UNLOCKS.", 2.5f);
    }

    private void UpdateAnimations()
    {
        float dt = Time.deltaTime;

        if (doorPivot != null)
        {
            doorAngle = Mathf.Lerp(
                doorAngle,
                doorTarget,
                1f - Mathf.Exp(-doorSpeed * dt)
            );

            if (Mathf.Abs(doorAngle - doorTarget) < 0.05f)
                doorAngle = doorTarget;

            doorPivot.localRotation = Quaternion.Euler(0f, doorAngle, 0f);
        }

        for (int i = 0; i < drawers.Count; i++)
        {
            DrawerData d = drawers[i];

            if (d.root == null)
                continue;

            Vector3 goal = d.isOpen ? d.openPos : d.closedPos;

            d.root.localPosition = Vector3.Lerp(
                d.root.localPosition,
                goal,
                1f - Mathf.Exp(-9f * dt)
            );
        }

        if (boxLidPivot != null)
        {
            lidAngle = Mathf.Lerp(
                lidAngle,
                lidTarget,
                1f - Mathf.Exp(-4f * dt)
            );

            boxLidPivot.localRotation = Quaternion.Euler(lidAngle, 0f, 0f);
        }

        if (phoneRinging && phoneHandset != null)
        {
            phoneHandset.localRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Sin(Time.time * 55f) * 3f
                );

            SetLampEmission(
                Mathf.Sin(Time.time * 10f) > 0f ? 4f : 0.4f
            );
        }

        if (kidnapperObj != null &&
            kidnapperObj.activeSelf &&
            kidnapperModel != null)
        {
            kidnapperModel.localRotation =
                Quaternion.Euler(
                    0f,
                    0f,
                    Mathf.Sin(Time.time * 0.9f) * 1.2f
                );
        }
    }

    private void StopRing()
    {
        if (phoneAudio != null)
            phoneAudio.Stop();

        if (phoneHandset != null)
            phoneHandset.localRotation = Quaternion.identity;

        SetLampEmission(0.3f);
    }

    private void SetLampEmission(float strength)
    {
        if (phoneLamp == null) return;

        phoneLamp.material.SetColor(
            "_EmissionColor",
            new Color(1f, 0.1f, 0.05f) * strength
        );
    }

    private void EnsureGameplayFloor()
    {
        if (worldRoot == null) return;

        Vector3 probe = new Vector3(0f, 0.08f, -2f);

        if (Physics.Raycast(
            probe + Vector3.up * 0.5f,
            Vector3.down,
            out RaycastHit hit,
            1.5f,
            ~0,
            QueryTriggerInteraction.Ignore))
        {
            if (hit.collider != null &&
                hit.collider.transform.IsChildOf(worldRoot.transform))
                return;
        }

        GameObject floor =
            GameObject.CreatePrimitive(PrimitiveType.Cube);

        floor.name = "Gameplay Floor Safety";
        floor.transform.SetParent(worldRoot.transform, false);
        floor.transform.position = new Vector3(0f, -0.1f, 0f);
        floor.transform.localScale =
            new Vector3(roomW + 1f, 0.2f, roomD + 1f);

        Renderer r = floor.GetComponent<Renderer>();

        if (r != null)
            r.enabled = false;
    }

    private void RebuildWorld()
    {
        if (worldRoot != null)
            Destroy(worldRoot);

        BuildWorld();
        BuildInteriorLighting();
        EnsureGameplayFloor();
    }

    private void ApplyAmbient()
    {
        // Directional ambient: slightly brighter from above, darker from below.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.20f, 0.21f, 0.24f);
        RenderSettings.ambientEquatorColor = new Color(0.14f, 0.15f, 0.17f);
        RenderSettings.ambientGroundColor = new Color(0.06f, 0.06f, 0.07f);
    }

    // Cool moonlight entering through a window, aimed across the floor.
    private void AddMoonSpot(string lightName, Vector3 pos, Vector3 target)
    {
        GameObject go = new GameObject(lightName);
        go.transform.SetParent(worldRoot.transform, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation((target - pos).normalized);

        Light l = go.AddComponent<Light>();

        l.type = LightType.Spot;
        l.color = new Color(0.55f, 0.68f, 1f);
        l.intensity = 3.5f;
        l.range = 9f;
        l.spotAngle = 64f;
        l.innerSpotAngle = 28f;
        l.shadows = LightShadows.Soft;
        l.shadowStrength = 0.85f;
    }

    private void BuildMoonShafts()
    {
        float hw = roomW / 2f;

        AddMoonSpot(
            "Moon Window Right",
            new Vector3(hw - 0.45f, 1.9f, 1.6f),
            new Vector3(0.8f, 0f, 0.9f)
        );

        AddMoonSpot(
            "Moon Window Left",
            new Vector3(-hw + 0.45f, 1.9f, -1.4f),
            new Vector3(-0.5f, 0f, -0.2f)
        );
    }

    // A few drifting dust motes. Skipped silently if the shader is unavailable.
    private void BuildDust()
    {
        if (worldRoot == null) return;

        Shader sh = Shader.Find("Sprites/Default");

        if (sh == null) return;

        GameObject go = new GameObject("Dust Motes");
        go.transform.SetParent(worldRoot.transform, false);
        go.transform.position = new Vector3(0f, roomH * 0.5f, 0f);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 20f;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = false;
        main.startLifetime = 14f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        main.startColor = new Color(1f, 0.92f, 0.75f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 70;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 5f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(roomW - 1f, roomH - 0.6f, roomD - 1f);

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.12f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.1f;

        Gradient g = new Gradient();
        g.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(1f, 0.2f),
                new GradientAlphaKey(1f, 0.8f),
                new GradientAlphaKey(0f, 1f)
            }
        );

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        col.color = new ParticleSystem.MinMaxGradient(g);

        ParticleSystemRenderer pr = go.GetComponent<ParticleSystemRenderer>();

        Material mat = new Material(sh);
        mat.mainTexture = GetSoftDotTexture();

        pr.material = mat;
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;

        ps.Play();
    }

    // Final pass over everything in the world: roughness, metal and wood grain.
    // Runs after the interior lighting so chandeliers and lamps are included.
    private void PolishWorld()
    {
        if (worldRoot == null) return;

        Renderer[] rs = worldRoot.GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < rs.Length; i++)
        {
            MeshRenderer mr = rs[i] as MeshRenderer;

            if (mr == null) continue;

            GameObject go = mr.gameObject;
            string n = go.name;
            string pn = go.transform.parent != null ? go.transform.parent.name : "";

            // Outdoors stays matte.
            if (exteriorRoot != null && go.transform.IsChildOf(exteriorRoot.transform))
            {
                Surface(go, 0.05f, 0f);
                continue;
            }

            // Brass hardware and key.
            if (n == "Handle" || n == "Knob" || n == "Latch" || n == "Keyhole Plate" ||
                n == "Bow" || n == "Shaft" || n == "Tooth 1" || n == "Tooth 2")
            {
                Surface(go, 0.62f, 0.55f);
                continue;
            }

            if (n == "Glass")
            {
                Surface(go, 0.92f, 0f);
                continue;
            }

            // Bakelite phone.
            if (pn == "Phone" || pn == "Handset")
            {
                Surface(go, n == "Dial" ? 0.5f : 0.78f, 0f);
                continue;
            }

            // Chandelier and lamps.
            if (pn == "Chandelier")
            {
                if (n != "Bulb" && n != "Rosette")
                    Surface(go, 0.55f, 0.6f);

                continue;
            }

            if (pn == "Floor Lamp")
            {
                if (n == "Base" || n == "Pole")
                    Surface(go, 0.55f, 0.6f);
                else
                    Surface(go, 0.1f, 0f);

                continue;
            }

            if (n == "Mug")
            {
                Surface(go, 0.7f, 0f);
                continue;
            }

            // Cloth and rugs are fully matte.
            if (n == "Mattress" || n == "Pillow" ||
                n.StartsWith("Rug") || n.StartsWith("Curtain"))
            {
                Surface(go, 0.02f, 0f);
                continue;
            }

            if (n == "Barrel")
            {
                Surface(go, 0.25f, 0.35f);
                continue;
            }

            if (n.StartsWith("Frame") || n.StartsWith("Bar ") || n == "Sill")
            {
                Surface(go, 0.35f, 0f);
                continue;
            }

            // Wood furniture.
            bool wood = false;
            bool alongU = false;

            if (n == "Crate")
            {
                wood = true;
            }
            else if (pn == "Dresser" || pn == "Nightstand")
            {
                wood = n != "Drawer Line";
            }
            else if (pn == "Table")
            {
                wood = n == "Top" || n == "Leg";
            }
            else if (pn.StartsWith("Drawer"))
            {
                wood = true;
                alongU = n == "Front";
            }
            else if (pn == "Puzzle Box" || pn == "Lid Pivot")
            {
                wood = n != "Lining";
            }
            else if (pn == "Exit Door")
            {
                wood = true;
                alongU = true;
            }

            if (wood)
                WoodSurface(go, alongU);
        }
    }

    private void BuildInteriorLighting()
    {
        if (worldRoot == null) return;

        flickerLight = null;

        float[] xs = { -3.2f, 0f, 3.2f };
        float[] intensities = { 2.8f, 3.4f, 2.8f };

        Color[] colors =
        {
            new Color(1f, 0.90f, 0.78f),
            new Color(1f, 0.88f, 0.72f),
            new Color(1f, 0.92f, 0.82f)
        };

        for (int i = 0; i < xs.Length; i++)
        {
            GameObject lightObj =
                new GameObject("Room Light " + i);

            lightObj.transform.SetParent(
                worldRoot.transform,
                false
            );

            lightObj.transform.position =
                new Vector3(
                    xs[i],
                    roomH - 0.45f,
                    0.3f
                );

            Light light =
                lightObj.AddComponent<Light>();

            light.type = LightType.Point;
            light.color = colors[i];
            light.intensity = intensities[i];
            light.range = 8f;

            // Only the centre chandelier casts real shadows: stronger contrast
            // and depth without paying for three shadowed point lights.
            light.shadows = (i == 1) ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = 0.7f;

            if (i == 0)
            {
                flickerLight = light;
                flickerBase = light.intensity;
            }

            BuildChandelier(worldRoot.transform, lightObj.transform.position);
        }

        // Two floor lamps in free corners add warm local light and depth.
        BuildFloorLamp(worldRoot.transform, roomW / 2f - 0.55f, roomD / 2f - 0.7f);
        BuildFloorLamp(worldRoot.transform, -roomW / 2f + 0.5f, -1.4f);

        BuildMoonShafts();
        ApplyAmbient();
        BuildDust();
        PolishWorld();
    }

    private void BuildWorld()
    {
        interactables.Clear();
        drawers.Clear();

        phoneObj = null;
        phoneHandset = null;
        phoneLamp = null;
        phoneAudio = null;
        doorPivot = null;
        boxLidPivot = null;
        boxTrigger = null;
        keyObj = null;
        kidnapperObj = null;
        kidnapperModel = null;
        exteriorRoot = null;

        doorAngle = 0f;
        doorTarget = 0f;
        doorSpeed = 4f;

        lidAngle = 0f;
        lidTarget = 0f;

        worldRoot = new GameObject("NINETY World");

        BuildRoomShell();

        BuildPhone();
        BuildTable();
        BuildEnvelope();
        BuildDresser();
        BuildDecor();
        BuildDoor();
        BuildKidnapper();
        BuildExterior();
    }

    private GameObject Part(
        Transform parent,
        PrimitiveType type,
        string partName,
        Vector3 pos,
        Vector3 scale,
        Color color,
        bool collider)
    {
        return Part(
            parent,
            type,
            partName,
            pos,
            scale,
            Vector3.zero,
            color,
            collider
        );
    }

    private GameObject Part(
        Transform parent,
        PrimitiveType type,
        string partName,
        Vector3 pos,
        Vector3 scale,
        Vector3 euler,
        Color color,
        bool collider)
    {
        GameObject go =
            GameObject.CreatePrimitive(type);

        go.name = partName;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation =
            Quaternion.Euler(euler);
        go.transform.localScale = scale;

        Renderer r = go.GetComponent<Renderer>();

        Material mat = r.material;
        mat.color = color;

        // Matte by default; PolishWorld raises it where a surface should shine.
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", 0.12f);

        if (!collider)
        {
            Collider c = go.GetComponent<Collider>();

            if (c != null)
                Destroy(c);
        }

        return go;
    }

    private void Glow(GameObject go, Color emission)
    {
        Renderer r = go.GetComponent<Renderer>();

        if (r == null) return;

        r.material.EnableKeyword("_EMISSION");
        r.material.SetColor(
            "_EmissionColor",
            emission
        );
    }

    private void Register(Collider c, Kind kind, int index)
    {
        Interactable it = new Interactable();
        it.kind = kind;
        it.index = index;

        interactables[c] = it;
    }

    private BoxCollider AddTrigger(
        GameObject go,
        Vector3 center,
        Vector3 size,
        Kind kind,
        int index)
    {
        BoxCollider bc =
            go.AddComponent<BoxCollider>();

        bc.isTrigger = true;
        bc.center = center;
        bc.size = size;

        Register(bc, kind, index);

        return bc;
    }

    private void BuildPhone()
    {
        Transform p = worldRoot.transform;

        Color wood = new Color(0.20f, 0.13f, 0.08f);
        Color darkWood = new Color(0.13f, 0.08f, 0.05f);
        Color phoneRed = new Color(0.33f, 0.05f, 0.05f);
        Color cream = new Color(0.75f, 0.72f, 0.64f);

        Vector3 pos = new Vector3(-1.0f, 0f, -1.2f);

        GameObject stand =
            new GameObject("Nightstand");

        stand.transform.SetParent(p, false);
        stand.transform.position = pos;

        Part(
            stand.transform,
            PrimitiveType.Cube,
            "Body",
            new Vector3(0f, 0.25f, 0f),
            new Vector3(0.5f, 0.5f, 0.42f),
            wood,
            true
        );

        Part(
            stand.transform,
            PrimitiveType.Cube,
            "Top",
            new Vector3(0f, 0.515f, 0f),
            new Vector3(0.55f, 0.03f, 0.47f),
            darkWood,
            false
        );

        Part(
            stand.transform,
            PrimitiveType.Cube,
            "Drawer Line",
            new Vector3(0f, 0.33f, -0.212f),
            new Vector3(0.42f, 0.012f, 0.005f),
            Color.black,
            false
        );

        Part(
            stand.transform,
            PrimitiveType.Sphere,
            "Knob",
            new Vector3(0f, 0.27f, -0.222f),
            new Vector3(0.035f, 0.035f, 0.035f),
            new Color(0.7f, 0.55f, 0.2f),
            false
        );

        phoneObj = new GameObject("Phone");
        phoneObj.transform.SetParent(p, false);
        phoneObj.transform.position =
            new Vector3(pos.x, 0.53f, pos.z);
        phoneObj.transform.rotation =
            Quaternion.Euler(0f, -20f, 0f);

        Transform ph = phoneObj.transform;

        Part(
            ph,
            PrimitiveType.Cube,
            "Base",
            new Vector3(0f, 0.025f, 0f),
            new Vector3(0.2f, 0.05f, 0.15f),
            phoneRed,
            false
        );

        Part(
            ph,
            PrimitiveType.Cylinder,
            "Dial",
            new Vector3(0f, 0.052f, 0.025f),
            new Vector3(0.09f, 0.003f, 0.09f),
            cream,
            false
        );

        GameObject handset =
            new GameObject("Handset");

        handset.transform.SetParent(ph, false);
        handset.transform.localPosition =
            new Vector3(0f, 0.065f, -0.035f);

        phoneHandset = handset.transform;

        Part(
            phoneHandset,
            PrimitiveType.Cube,
            "Bar",
            Vector3.zero,
            new Vector3(0.19f, 0.025f, 0.04f),
            phoneRed,
            false
        );

        Part(
            phoneHandset,
            PrimitiveType.Cube,
            "End L",
            new Vector3(-0.095f, -0.012f, 0f),
            new Vector3(0.05f, 0.045f, 0.05f),
            phoneRed,
            false
        );

        Part(
            phoneHandset,
            PrimitiveType.Cube,
            "End R",
            new Vector3(0.095f, -0.012f, 0f),
            new Vector3(0.05f, 0.045f, 0.05f),
            phoneRed,
            false
        );

        GameObject lamp =
            Part(
                ph,
                PrimitiveType.Sphere,
                "Lamp",
                new Vector3(0.07f, 0.052f, 0.05f),
                new Vector3(0.018f, 0.018f, 0.018f),
                new Color(0.6f, 0.05f, 0.05f),
                false
            );

        phoneLamp =
            lamp.GetComponent<Renderer>();

        Glow(
            lamp,
            new Color(1f, 0.1f, 0.05f) * 0.3f
        );

        AddTrigger(
            phoneObj,
            new Vector3(0f, 0.05f, 0f),
            new Vector3(0.34f, 0.16f, 0.28f),
            Kind.Phone,
            0
        );

        phoneAudio =
            phoneObj.AddComponent<AudioSource>();

        phoneAudio.clip = ringClip;
        phoneAudio.loop = true;
        phoneAudio.playOnAwake = false;
        phoneAudio.spatialBlend = 1f;
        phoneAudio.minDistance = 1.5f;
        phoneAudio.maxDistance = 16f;
        phoneAudio.rolloffMode =
            AudioRolloffMode.Linear;
        phoneAudio.volume = 0.9f;
    }

    private void BuildTable()
    {
        Transform p = worldRoot.transform;

        Color wood = new Color(0.20f, 0.13f, 0.08f);
        Color darkWood = new Color(0.13f, 0.08f, 0.05f);

        tablePos =
            new Vector3(
                -roomW / 2f + 1.7f,
                0f,
                0.5f
            );

        GameObject table =
            new GameObject("Table");

        table.transform.SetParent(p, false);
        table.transform.position = tablePos;

        Transform t = table.transform;

        Part(
            t,
            PrimitiveType.Cube,
            "Top",
            new Vector3(0f, 0.745f, 0f),
            new Vector3(0.9f, 0.05f, 1.5f),
            wood,
            true
        );

        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sz = -1; sz <= 1; sz += 2)
            {
                Part(
                    t,
                    PrimitiveType.Cube,
                    "Leg",
                    new Vector3(sx * 0.4f, 0.36f, sz * 0.7f),
                    new Vector3(0.06f, 0.72f, 0.06f),
                    darkWood,
                    false
                );
            }
        }

        Part(
            t,
            PrimitiveType.Cube,
            "Book",
            new Vector3(0.1f, 0.795f, -0.3f),
            new Vector3(0.22f, 0.04f, 0.3f),
            new Vector3(0f, 25f, 0f),
            new Color(0.22f, 0.06f, 0.06f),
            false
        );

        Part(
            t,
            PrimitiveType.Cylinder,
            "Mug",
            new Vector3(-0.15f, 0.805f, 0.35f),
            new Vector3(0.08f, 0.035f, 0.08f),
            new Color(0.6f, 0.58f, 0.52f),
            false
        );
    }

    private void BuildEnvelope()
    {
        Color paper = new Color(0.88f, 0.84f, 0.70f);

        GameObject env =
            new GameObject("Envelope");

        env.transform.SetParent(worldRoot.transform, false);
        env.transform.position =
            new Vector3(
                tablePos.x + 0.25f,
                0.005f,
                tablePos.z - 0.15f
            );

        env.transform.rotation =
            Quaternion.Euler(0f, 20f, 0f);

        Transform e = env.transform;

        GameObject body =
            Part(
                e,
                PrimitiveType.Cube,
                "Paper",
                Vector3.zero,
                new Vector3(0.24f, 0.008f, 0.16f),
                paper,
                false
            );

        Glow(body, paper * 0.22f);

        Part(
            e,
            PrimitiveType.Cube,
            "Flap Line",
            new Vector3(0f, 0.0045f, 0.035f),
            new Vector3(0.24f, 0.002f, 0.004f),
            new Color(0.55f, 0.5f, 0.4f),
            false
        );

        Part(
            e,
            PrimitiveType.Cylinder,
            "Seal",
            new Vector3(0f, 0.005f, 0.035f),
            new Vector3(0.035f, 0.002f, 0.035f),
            new Color(0.45f, 0.05f, 0.05f),
            false
        );

        AddTrigger(
            env,
            new Vector3(0f, 0.06f, 0f),
            new Vector3(0.34f, 0.14f, 0.26f),
            Kind.Envelope,
            0
        );
    }

    private void BuildDresser()
    {
        Color wood = new Color(0.20f, 0.13f, 0.08f);
        Color darkWood = new Color(0.13f, 0.08f, 0.05f);
        Color brass = new Color(0.72f, 0.55f, 0.2f);

        float w = 1.1f;
        float d = 0.5f;
        float h = 0.9f;
        float cellH = 0.25f;
        float gapH = 0.02f;
        float y0 = 0.1f;

        GameObject dresser =
            new GameObject("Dresser");

        dresser.transform.SetParent(
            worldRoot.transform,
            false
        );

        dresser.transform.position =
            new Vector3(
                roomW * 0.26f,
                0f,
                roomD / 2f - d / 2f - 0.05f
            );

        Transform dt = dresser.transform;

        Part(
            dt,
            PrimitiveType.Cube,
            "Left",
            new Vector3(-w / 2f + 0.02f, h / 2f, 0f),
            new Vector3(0.04f, h, d),
            wood,
            true
        );

        Part(
            dt,
            PrimitiveType.Cube,
            "Right",
            new Vector3(w / 2f - 0.02f, h / 2f, 0f),
            new Vector3(0.04f, h, d),
            wood,
            true
        );

        Part(
            dt,
            PrimitiveType.Cube,
            "Back",
            new Vector3(0f, h / 2f, d / 2f - 0.015f),
            new Vector3(w, h, 0.03f),
            darkWood,
            true
        );

        Part(
            dt,
            PrimitiveType.Cube,
            "Top",
            new Vector3(0f, h + 0.02f, 0f),
            new Vector3(w + 0.04f, 0.04f, d + 0.04f),
            darkWood,
            true
        );

        Part(
            dt,
            PrimitiveType.Cube,
            "Plinth",
            new Vector3(0f, 0.05f, 0f),
            new Vector3(w, 0.1f, d),
            darkWood,
            true
        );

        for (int i = 0; i < 2; i++)
        {
            float yd =
                y0 +
                i * (cellH + gapH) +
                cellH +
                gapH / 2f;

            Part(
                dt,
                PrimitiveType.Cube,
                "Divider",
                new Vector3(0f, yd, 0f),
                new Vector3(w - 0.08f, gapH, d - 0.03f),
                darkWood,
                false
            );
        }

        for (int i = 0; i < 3; i++)
        {
            float yb =
                y0 +
                i * (cellH + gapH);

            GameObject drawer =
                new GameObject("Drawer " + i);

            drawer.transform.SetParent(dt, false);

            Vector3 closed =
                new Vector3(
                    0f,
                    yb,
                    -d / 2f
                );

            drawer.transform.localPosition =
                closed;

            Transform dr = drawer.transform;

            GameObject front =
                Part(
                    dr,
                    PrimitiveType.Cube,
                    "Front",
                    new Vector3(
                        0f,
                        cellH / 2f,
                        0.0125f
                    ),
                    new Vector3(
                        w - 0.08f,
                        cellH - 0.01f,
                        0.025f
                    ),
                    wood,
                    true
                );

            Register(
                front.GetComponent<Collider>(),
                Kind.Drawer,
                i
            );

            Part(
                dr,
                PrimitiveType.Cube,
                "Handle",
                new Vector3(
                    0f,
                    cellH / 2f,
                    -0.012f
                ),
                new Vector3(
                    0.14f,
                    0.02f,
                    0.025f
                ),
                brass,
                false
            );

            float trayW = w - 0.12f;

            Part(
                dr,
                PrimitiveType.Cube,
                "Bottom",
                new Vector3(0f, 0.02f, 0.235f),
                new Vector3(trayW, 0.015f, 0.42f),
                darkWood,
                false
            );

            Part(
                dr,
                PrimitiveType.Cube,
                "Side L",
                new Vector3(
                    -trayW / 2f + 0.0075f,
                    0.115f,
                    0.235f
                ),
                new Vector3(
                    0.015f,
                    0.19f,
                    0.42f
                ),
                darkWood,
                false
            );

            Part(
                dr,
                PrimitiveType.Cube,
                "Side R",
                new Vector3(
                    trayW / 2f - 0.0075f,
                    0.115f,
                    0.235f
                ),
                new Vector3(
                    0.015f,
                    0.19f,
                    0.42f
                ),
                darkWood,
                false
            );

            Part(
                dr,
                PrimitiveType.Cube,
                "Back",
                new Vector3(
                    0f,
                    0.115f,
                    0.445f
                ),
                new Vector3(
                    trayW,
                    0.19f,
                    0.015f
                ),
                darkWood,
                false
            );

            DrawerData data =
                new DrawerData();

            data.root = dr;
            data.closedPos = closed;
            data.openPos =
                closed +
                new Vector3(
                    0f,
                    0f,
                    -DrawerSlide
                );

            data.isOpen = false;

            drawers.Add(data);

            if (i == 1)
                BuildPuzzleBox(dr);
        }
    }

    private void BuildPuzzleBox(Transform drawer)
    {
        Color walnut = new Color(0.16f, 0.09f, 0.05f);
        Color brass = new Color(0.72f, 0.55f, 0.2f);
        Color velvet = new Color(0.25f, 0.03f, 0.04f);

        GameObject box =
            new GameObject("Puzzle Box");

        box.transform.SetParent(drawer, false);
        box.transform.localPosition =
            new Vector3(0f, 0.0275f, 0.16f);

        Transform b = box.transform;

        Part(
            b,
            PrimitiveType.Cube,
            "Base",
            new Vector3(0f, 0.01f, 0f),
            new Vector3(0.28f, 0.02f, 0.2f),
            walnut,
            false
        );

        Part(
            b,
            PrimitiveType.Cube,
            "Wall Front",
            new Vector3(0f, 0.055f, -0.0925f),
            new Vector3(0.28f, 0.07f, 0.015f),
            walnut,
            false
        );

        Part(
            b,
            PrimitiveType.Cube,
            "Wall Back",
            new Vector3(0f, 0.055f, 0.0925f),
            new Vector3(0.28f, 0.07f, 0.015f),
            walnut,
            false
        );

        Part(
            b,
            PrimitiveType.Cube,
            "Wall Left",
            new Vector3(-0.1325f, 0.055f, 0f),
            new Vector3(0.015f, 0.07f, 0.17f),
            walnut,
            false
        );

        Part(
            b,
            PrimitiveType.Cube,
            "Wall Right",
            new Vector3(0.1325f, 0.055f, 0f),
            new Vector3(0.015f, 0.07f, 0.17f),
            walnut,
            false
        );

        Part(
            b,
            PrimitiveType.Cube,
            "Lining",
            new Vector3(0f, 0.022f, 0f),
            new Vector3(0.25f, 0.004f, 0.17f),
            velvet,
            false
        );

        GameObject lidPivot =
            new GameObject("Lid Pivot");

        lidPivot.transform.SetParent(b, false);
        lidPivot.transform.localPosition =
            new Vector3(0f, 0.09f, 0.1f);

        boxLidPivot = lidPivot.transform;

        Part(
            boxLidPivot,
            PrimitiveType.Cube,
            "Lid",
            new Vector3(0f, 0.0125f, -0.1f),
            new Vector3(0.28f, 0.025f, 0.2f),
            walnut,
            false
        );

        Part(
            boxLidPivot,
            PrimitiveType.Cube,
            "Latch",
            new Vector3(0f, 0.005f, -0.205f),
            new Vector3(0.04f, 0.035f, 0.012f),
            brass,
            false
        );

        boxTrigger =
            AddTrigger(
                box,
                new Vector3(0f, 0.07f, 0f),
                new Vector3(0.34f, 0.18f, 0.26f),
                Kind.Box,
                0
            );

        keyObj =
            new GameObject("Door Key");

        keyObj.transform.SetParent(b, false);
        keyObj.transform.localPosition =
            new Vector3(0f, 0.035f, 0f);

        Transform k = keyObj.transform;

        GameObject bow =
            Part(
                k,
                PrimitiveType.Cylinder,
                "Bow",
                new Vector3(-0.055f, 0f, 0f),
                new Vector3(0.045f, 0.004f, 0.045f),
                brass,
                false
            );

        Part(
            k,
            PrimitiveType.Cylinder,
            "Bow Hole",
            new Vector3(-0.055f, 0.0005f, 0f),
            new Vector3(0.02f, 0.0045f, 0.02f),
            velvet,
            false
        );

        GameObject shaft =
            Part(
                k,
                PrimitiveType.Cube,
                "Shaft",
                new Vector3(0.02f, 0f, 0f),
                new Vector3(0.1f, 0.008f, 0.012f),
                brass,
                false
            );

        GameObject tooth1 =
            Part(
                k,
                PrimitiveType.Cube,
                "Tooth 1",
                new Vector3(0.055f, 0f, 0.014f),
                new Vector3(0.012f, 0.008f, 0.02f),
                brass,
                false
            );

        GameObject tooth2 =
            Part(
                k,
                PrimitiveType.Cube,
                "Tooth 2",
                new Vector3(0.035f, 0f, 0.014f),
                new Vector3(0.012f, 0.008f, 0.02f),
                brass,
                false
            );

        Color keyGlow = brass * 0.5f;

        Glow(bow, keyGlow);
        Glow(shaft, keyGlow);
        Glow(tooth1, keyGlow);
        Glow(tooth2, keyGlow);

        AddTrigger(
            keyObj,
            new Vector3(0f, 0.01f, 0f),
            new Vector3(0.2f, 0.06f, 0.1f),
            Kind.Key,
            0
        );

        keyObj.SetActive(false);
    }

    private void BuildDecor()
    {
        Transform p = worldRoot.transform;

        float hw = roomW / 2f;
        float hd = roomD / 2f;

        Color crate = new Color(0.22f, 0.15f, 0.09f);
        Color rust = new Color(0.22f, 0.10f, 0.07f);
        Color cloth = new Color(0.32f, 0.30f, 0.26f);
        Color frame = new Color(0.10f, 0.10f, 0.10f);

        Part(
            p,
            PrimitiveType.Cube,
            "Crate",
            new Vector3(-hw + 1f, 0.35f, hd - 0.8f),
            new Vector3(0.7f, 0.7f, 0.7f),
            new Vector3(0f, 10f, 0f),
            crate,
            true
        );

        Part(
            p,
            PrimitiveType.Cube,
            "Crate",
            new Vector3(-hw + 1.85f, 0.35f, hd - 0.6f),
            new Vector3(0.7f, 0.7f, 0.7f),
            new Vector3(0f, -15f, 0f),
            crate,
            true
        );

        Part(
            p,
            PrimitiveType.Cube,
            "Crate",
            new Vector3(-hw + 1.1f, 1.0f, hd - 0.8f),
            new Vector3(0.6f, 0.6f, 0.6f),
            new Vector3(0f, 30f, 0f),
            crate,
            true
        );

        Part(
            p,
            PrimitiveType.Cylinder,
            "Barrel",
            new Vector3(-hw + 0.7f, 0.45f, -hd + 0.9f),
            new Vector3(0.6f, 0.45f, 0.6f),
            rust,
            true
        );

        Part(
            p,
            PrimitiveType.Cube,
            "Bed Base",
            new Vector3(hw - 1f, 0.17f, -1.8f),
            new Vector3(1.1f, 0.34f, 2.1f),
            frame,
            true
        );

        Part(
            p,
            PrimitiveType.Cube,
            "Mattress",
            new Vector3(hw - 1f, 0.37f, -1.8f),
            new Vector3(1f, 0.1f, 2f),
            cloth,
            false
        );

        Part(
            p,
            PrimitiveType.Cube,
            "Pillow",
            new Vector3(hw - 1f, 0.46f, -2.55f),
            new Vector3(0.7f, 0.1f, 0.35f),
            new Color(0.45f, 0.43f, 0.40f),
            false
        );
    }

    private void BuildDoor()
    {
        Color doorWood = new Color(0.20f, 0.12f, 0.07f);
        Color panelWood = new Color(0.15f, 0.09f, 0.05f);
        Color brass = new Color(0.72f, 0.55f, 0.2f);

        float gap = 0.02f;
        float panelW = doorW - gap;
        float panelH = doorH - gap;
        float panelT = roomT * 0.5f;
        float zFront = -(roomD / 2f + roomT / 2f);

        GameObject pivot =
            new GameObject("Exit Door");

        pivot.transform.SetParent(
            worldRoot.transform,
            false
        );

        pivot.transform.position =
            new Vector3(
                -doorW / 2f + gap / 2f,
                0f,
                zFront
            );

        doorPivot = pivot.transform;

        GameObject panel =
            Part(
                doorPivot,
                PrimitiveType.Cube,
                "Door Panel",
                new Vector3(
                    panelW / 2f,
                    panelH / 2f,
                    0f
                ),
                new Vector3(
                    panelW,
                    panelH,
                    panelT
                ),
                doorWood,
                true
            );

        // Door is always opaque.
        Renderer doorRenderer =
            panel.GetComponent<Renderer>();

        if (doorRenderer != null)
        {
            Material mat = doorRenderer.material;

            mat.color =
                new Color(
                    doorWood.r,
                    doorWood.g,
                    doorWood.b,
                    1f
                );

            if (mat.HasProperty("_BaseColor"))
            {
                mat.SetColor(
                    "_BaseColor",
                    new Color(
                        doorWood.r,
                        doorWood.g,
                        doorWood.b,
                        1f
                    )
                );
            }

            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 0f);

            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);

            mat.renderQueue = 2000;
        }

        Register(
            panel.GetComponent<Collider>(),
            Kind.Door,
            0
        );

        float faceZ =
            panelT / 2f + 0.005f;

        Part(
            doorPivot,
            PrimitiveType.Cube,
            "Inset Lower",
            new Vector3(
                panelW / 2f,
                0.65f,
                faceZ
            ),
            new Vector3(
                0.8f,
                0.9f,
                0.01f
            ),
            panelWood,
            false
        );

        Part(
            doorPivot,
            PrimitiveType.Cube,
            "Inset Upper",
            new Vector3(
                panelW / 2f,
                1.75f,
                faceZ
            ),
            new Vector3(
                0.8f,
                0.8f,
                0.01f
            ),
            panelWood,
            false
        );

        Part(
            doorPivot,
            PrimitiveType.Cube,
            "Handle",
            new Vector3(
                panelW - 0.2f,
                1.05f,
                faceZ + 0.03f
            ),
            new Vector3(
                0.14f,
                0.03f,
                0.05f
            ),
            brass,
            false
        );

        Part(
            doorPivot,
            PrimitiveType.Cube,
            "Keyhole Plate",
            new Vector3(
                panelW - 0.2f,
                1.2f,
                faceZ + 0.005f
            ),
            new Vector3(
                0.06f,
                0.1f,
                0.01f
            ),
            brass,
            false
        );

        // Opaque trim around the doorway.
        float trimZ =
            zFront + roomT / 2f + 0.015f;

        Part(
            worldRoot.transform,
            PrimitiveType.Cube,
            "Trim Left",
            new Vector3(
                -doorW / 2f - 0.04f,
                doorH / 2f,
                trimZ
            ),
            new Vector3(
                0.08f,
                doorH + 0.08f,
                0.03f
            ),
            panelWood,
            false
        );

        Part(
            worldRoot.transform,
            PrimitiveType.Cube,
            "Trim Right",
            new Vector3(
                doorW / 2f + 0.04f,
                doorH / 2f,
                trimZ
            ),
            new Vector3(
                0.08f,
                doorH + 0.08f,
                0.03f
            ),
            panelWood,
            false
        );

        Part(
            worldRoot.transform,
            PrimitiveType.Cube,
            "Trim Top",
            new Vector3(
                0f,
                doorH + 0.04f,
                trimZ
            ),
            new Vector3(
                doorW + 0.16f,
                0.08f,
                0.03f
            ),
            panelWood,
            false
        );
    }

    private void BuildKidnapper()
    {
        kidnapperObj =
            new GameObject("Kidnapper");

        kidnapperObj.transform.SetParent(
            worldRoot.transform,
            false
        );

        GameObject modelObj =
            new GameObject("Model");

        modelObj.transform.SetParent(
            kidnapperObj.transform,
            false
        );

        kidnapperModel =
            modelObj.transform;

        Transform m = kidnapperModel;

        Color jacket = new Color(0.09f, 0.09f, 0.10f);
        Color trousers = new Color(0.07f, 0.07f, 0.085f);
        Color shoes = new Color(0.03f, 0.03f, 0.03f);
        Color glove = new Color(0.04f, 0.04f, 0.04f);
        Color mask = new Color(0.05f, 0.05f, 0.055f);
        Color skin = new Color(0.62f, 0.50f, 0.43f);

        Part(m, PrimitiveType.Cube, "Shoe L",
            new Vector3(-0.1f, 0.04f, 0.05f),
            new Vector3(0.11f, 0.08f, 0.27f),
            shoes, false);

        Part(m, PrimitiveType.Cube, "Shoe R",
            new Vector3(0.1f, 0.04f, 0.05f),
            new Vector3(0.11f, 0.08f, 0.27f),
            shoes, false);

        Part(m, PrimitiveType.Capsule, "Shin L",
            new Vector3(-0.1f, 0.29f, 0f),
            new Vector3(0.13f, 0.21f, 0.13f),
            trousers, false);

        Part(m, PrimitiveType.Capsule, "Shin R",
            new Vector3(0.1f, 0.29f, 0f),
            new Vector3(0.13f, 0.21f, 0.13f),
            trousers, false);

        Part(m, PrimitiveType.Capsule, "Thigh L",
            new Vector3(-0.1f, 0.72f, 0f),
            new Vector3(0.17f, 0.24f, 0.17f),
            trousers, false);

        Part(m, PrimitiveType.Capsule, "Thigh R",
            new Vector3(0.1f, 0.72f, 0f),
            new Vector3(0.17f, 0.24f, 0.17f),
            trousers, false);

        Part(m, PrimitiveType.Cube, "Pelvis",
            new Vector3(0f, 0.98f, 0f),
            new Vector3(0.34f, 0.16f, 0.2f),
            trousers, false);

        Part(m, PrimitiveType.Cube, "Torso",
            new Vector3(0f, 1.32f, 0f),
            new Vector3(0.4f, 0.56f, 0.23f),
            jacket, false);

        Part(m, PrimitiveType.Capsule, "Shoulders",
            new Vector3(0f, 1.56f, 0f),
            new Vector3(0.13f, 0.26f, 0.13f),
            new Vector3(0f, 0f, 90f),
            jacket, false);

        Part(m, PrimitiveType.Capsule, "Upper Arm L",
            new Vector3(-0.285f, 1.36f, 0f),
            new Vector3(0.1f, 0.17f, 0.1f),
            new Vector3(0f, 0f, -3f),
            jacket, false);

        Part(m, PrimitiveType.Capsule, "Upper Arm R",
            new Vector3(0.285f, 1.36f, 0f),
            new Vector3(0.1f, 0.17f, 0.1f),
            new Vector3(0f, 0f, 3f),
            jacket, false);

        Part(m, PrimitiveType.Capsule, "Forearm L",
            new Vector3(-0.3f, 1.04f, 0.02f),
            new Vector3(0.085f, 0.16f, 0.085f),
            new Vector3(0f, 0f, -2f),
            jacket, false);

        Part(m, PrimitiveType.Capsule, "Forearm R",
            new Vector3(0.3f, 1.04f, 0.02f),
            new Vector3(0.085f, 0.16f, 0.085f),
            new Vector3(0f, 0f, 2f),
            jacket, false);

        Part(m, PrimitiveType.Sphere, "Hand L",
            new Vector3(-0.3f, 0.84f, 0.03f),
            new Vector3(0.09f, 0.11f, 0.08f),
            glove, false);

        Part(m, PrimitiveType.Sphere, "Hand R",
            new Vector3(0.3f, 0.84f, 0.03f),
            new Vector3(0.09f, 0.11f, 0.08f),
            glove, false);

        Part(m, PrimitiveType.Cylinder, "Neck",
            new Vector3(0f, 1.63f, 0f),
            new Vector3(0.1f, 0.04f, 0.1f),
            jacket, false);

        Part(m, PrimitiveType.Sphere, "Head",
            new Vector3(0f, 1.77f, 0f),
            new Vector3(0.19f, 0.23f, 0.21f),
            mask, false);

        Part(m, PrimitiveType.Cube, "Eye Strip",
            new Vector3(0f, 1.79f, 0.088f),
            new Vector3(0.14f, 0.035f, 0.03f),
            skin, false);

        Part(m, PrimitiveType.Sphere, "Eye L",
            new Vector3(-0.035f, 1.79f, 0.104f),
            new Vector3(0.022f, 0.022f, 0.012f),
            Color.black, false);

        Part(m, PrimitiveType.Sphere, "Eye R",
            new Vector3(0.035f, 1.79f, 0.104f),
            new Vector3(0.022f, 0.022f, 0.012f),
            Color.black, false);

        Part(m, PrimitiveType.Sphere, "Nose",
            new Vector3(0f, 1.76f, 0.105f),
            new Vector3(0.04f, 0.04f, 0.04f),
            mask, false);

        Part(m, PrimitiveType.Cube, "Mouth",
            new Vector3(0f, 1.72f, 0.098f),
            new Vector3(0.06f, 0.018f, 0.02f),
            skin, false);

        GameObject fill =
            new GameObject("Fill Light");

        fill.transform.SetParent(
            kidnapperObj.transform,
            false
        );

        fill.transform.localPosition =
            new Vector3(0f, 1.5f, 1.6f);

        Light fillLight =
            fill.AddComponent<Light>();

        fillLight.type = LightType.Point;
        fillLight.color =
            new Color(0.8f, 0.85f, 1f);
        fillLight.intensity = 1.4f;
        fillLight.range = 4f;
        fillLight.shadows = LightShadows.None;

        kidnapperObj.SetActive(false);
    }

    private void BuildExterior()
    {
        exteriorRoot =
            new GameObject("Exterior");

        exteriorRoot.transform.SetParent(
            worldRoot.transform,
            false
        );

        Transform ext =
            exteriorRoot.transform;

        float zStart =
            -(roomD / 2f + roomT);

        Color grass =
            new Color(0.03f, 0.06f, 0.035f);

        Color pathColor =
            new Color(0.17f, 0.16f, 0.14f);

        Color bark =
            new Color(0.12f, 0.08f, 0.05f);

        Color leaves =
            new Color(0.05f, 0.12f, 0.06f);

        Color leaves2 =
            new Color(0.06f, 0.15f, 0.07f);

        Part(
            ext,
            PrimitiveType.Cube,
            "Ground",
            new Vector3(
                0f,
                -0.1f,
                zStart - 35f
            ),
            new Vector3(80f, 0.2f, 70f),
            grass,
            false
        );

        Part(
            ext,
            PrimitiveType.Cube,
            "Path",
            new Vector3(
                0f,
                0.01f,
                zStart - 20f
            ),
            new Vector3(2.6f, 0.02f, 40f),
            pathColor,
            false
        );

        // A small, controlled set of trees: a few near the doorway and a dense
        // tree line behind them, so the view through the door is filled with
        // trees instead of a large empty black exterior.
        float[] treeX = { -3.4f, 3.6f, -4.4f, 4.6f, -2.6f, 2.4f, -7.5f, -4.5f, -1.5f, 1.5f, 4.5f, 7.5f };
        float[] treeD = { 9f, 11f, 13f, 15f, 17f, 18f, 22f, 22f, 22f, 22f, 22f, 22f };

        for (int i = 0; i < treeX.Length; i++)
        {
            float x = treeX[i];

            float z =
                zStart -
                treeD[i];

            float h =
                3.5f +
                2.5f * Hash(i * 5.3f);

            Part(
                ext,
                PrimitiveType.Cylinder,
                "Trunk",
                new Vector3(x, h / 2f, z),
                new Vector3(0.35f, h / 2f, 0.35f),
                bark,
                false
            );

            Part(
                ext,
                PrimitiveType.Sphere,
                "Canopy",
                new Vector3(x, h + 1f, z),
                new Vector3(3.2f, 3.6f, 3.2f),
                leaves,
                false
            );

            Part(
                ext,
                PrimitiveType.Sphere,
                "Canopy Top",
                new Vector3(x + 0.3f, h + 2.3f, z),
                new Vector3(2.2f, 2.4f, 2.2f),
                leaves2,
                false
            );
        }

        GameObject moon =
            Part(
                ext,
                PrimitiveType.Sphere,
                "Moon",
                new Vector3(
                    -20f,
                    26f,
                    zStart - 75f
                ),
                new Vector3(7f, 7f, 7f),
                new Color(0.85f, 0.9f, 1f),
                false
            );

        Glow(
            moon,
            new Color(0.85f, 0.9f, 1f) * 1.6f
        );

        Part(
            ext,
            PrimitiveType.Cylinder,
            "Lamp Pole",
            new Vector3(
                2.6f,
                1.6f,
                zStart - 6f
            ),
            new Vector3(0.08f, 1.6f, 0.08f),
            new Color(0.05f, 0.05f, 0.05f),
            false
        );

        GameObject lampBulb =
            Part(
                ext,
                PrimitiveType.Sphere,
                "Lamp Bulb",
                new Vector3(
                    2.6f,
                    3.3f,
                    zStart - 6f
                ),
                new Vector3(0.28f, 0.28f, 0.28f),
                new Color(1f, 0.85f, 0.55f),
                false
            );

        Glow(
            lampBulb,
            new Color(1f, 0.8f, 0.5f) * 2.5f
        );

        GameObject lampLightObj =
            new GameObject("Lamp Light");

        lampLightObj.transform.SetParent(
            ext,
            false
        );

        lampLightObj.transform.position =
            new Vector3(
                2.4f,
                3.1f,
                zStart - 6f
            );

        Light lampLight =
            lampLightObj.AddComponent<Light>();

        lampLight.type = LightType.Point;
        lampLight.color =
            new Color(1f, 0.78f, 0.5f);
        lampLight.intensity = 3f;
        lampLight.range = 11f;
        lampLight.shadows = LightShadows.None;

        GameObject moonLightObj =
            new GameObject("Moonlight");

        moonLightObj.transform.SetParent(
            ext,
            false
        );

        moonLightObj.transform.position =
            new Vector3(
                -3f,
                9f,
                zStart - 10f
            );

        Light moonLight =
            moonLightObj.AddComponent<Light>();

        moonLight.type = LightType.Point;
        moonLight.color =
            new Color(0.6f, 0.7f, 1f);
        moonLight.intensity = 2.2f;
        moonLight.range = 30f;
        moonLight.shadows = LightShadows.None;

        Part(
            ext,
            PrimitiveType.Cube,
            "Cabin",
            new Vector3(
                20f,
                1.75f,
                zStart - 50f
            ),
            new Vector3(6f, 3.5f, 5f),
            new Color(0.05f, 0.04f, 0.035f),
            false
        );

        GameObject window =
            Part(
                ext,
                PrimitiveType.Cube,
                "Cabin Window",
                new Vector3(
                    17.2f,
                    1.9f,
                    zStart - 47.45f
                ),
                new Vector3(1.1f, 0.9f, 0.1f),
                new Color(1f, 0.75f, 0.4f),
                false
            );

        Glow(
            window,
            new Color(1f, 0.7f, 0.35f) * 2f
        );

        exteriorRoot.SetActive(false);
    }

    private float Hash(float v)
    {
        return Mathf.Repeat(
            Mathf.Sin(v * 12.9898f) * 43758.5453f,
            1f
        );
    }

    private void BuildRingClip()
    {
        int rate = 22050;
        float seconds = 1.6f;
        int count = Mathf.CeilToInt(rate * seconds);

        float[] data = new float[count];

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            float v = 0f;

            if (t < 0.8f)
            {
                float env =
                    Mathf.Min(1f, t / 0.01f) *
                    Mathf.Min(1f, (0.8f - t) / 0.02f);

                float warble =
                    0.75f +
                    0.25f *
                    Mathf.Sin(
                        2f *
                        Mathf.PI *
                        20f *
                        t
                    );

                float tone =
                    Mathf.Sin(
                        2f *
                        Mathf.PI *
                        440f *
                        t
                    ) +
                    Mathf.Sin(
                        2f *
                        Mathf.PI *
                        480f *
                        t
                    );

                v =
                    tone *
                    0.28f *
                    env *
                    warble;
            }

            data[i] = v;
        }

        ringClip =
            AudioClip.Create(
                "PhoneRing",
                count,
                1,
                rate,
                false
            );

        ringClip.SetData(data, 0);
    }
}