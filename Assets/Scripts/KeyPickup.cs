using UnityEngine;
using UnityEngine.InputSystem;

public class KeyPickup : MonoBehaviour
{
    public static bool HasKey { get; private set; }

    private static string messageText = "";
    private static float messageEndTime;

    public float interactDistance = 3f;
    public float spinSpeed = 90f;
    public float bobHeight = 0.08f;
    public float bobSpeed = 2f;

    private Transform visuals;
    private bool collected;
    private bool isLookedAt;
    private GUIStyle promptStyle;
    private GUIStyle messageStyle;

    // Resets static values every time Play mode starts
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        HasKey = false;
        messageText = "";
        messageEndTime = 0f;
    }

    // Creates the key automatically after the scene loads
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateKey()
    {
if (FindAnyObjectByType<KeyPickup>() != null) return;
        float width = 10f;
        float depth = 8f;

RoomBuilder room = FindAnyObjectByType<RoomBuilder>();        if (room != null)
        {
            width = room.width;
            depth = room.depth;
        }

        GameObject keyObj = new GameObject("Key");
        keyObj.transform.position = new Vector3(width * 0.3f, 0.8f, depth * 0.3f);
        keyObj.AddComponent<KeyPickup>();
    }

    // Other scripts (like the door) can call this to show a message on screen
    public static void ShowMessage(string text, float duration = 2f)
    {
        messageText = text;
        messageEndTime = Time.time + duration;
    }

    void Awake()
    {
        BuildKey();
    }

    void BuildKey()
    {
        // Invisible box used for looking at the key and picking it up
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.size = new Vector3(0.6f, 0.4f, 0.6f);

        GameObject visualsObj = new GameObject("Key Visuals");
        visualsObj.transform.SetParent(transform, false);
        visuals = visualsObj.transform;

        Color gold = new Color(1f, 0.8f, 0.2f);

        // Shaft
        CreatePart(PrimitiveType.Cube, "Shaft", new Vector3(0f, 0f, 0f), new Vector3(0.35f, 0.04f, 0.06f), gold);
        // Round head
        CreatePart(PrimitiveType.Cylinder, "Head", new Vector3(-0.2f, 0f, 0f), new Vector3(0.16f, 0.02f, 0.16f), gold);
        // Teeth
        CreatePart(PrimitiveType.Cube, "Tooth 1", new Vector3(0.13f, 0f, -0.05f), new Vector3(0.05f, 0.04f, 0.05f), gold);
        CreatePart(PrimitiveType.Cube, "Tooth 2", new Vector3(0.06f, 0f, -0.05f), new Vector3(0.05f, 0.04f, 0.05f), gold);

        // Small glow so the key is easy to spot in the dark
        GameObject lightObj = new GameObject("Key Glow");
        lightObj.transform.SetParent(visuals, false);
        lightObj.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        Light glow = lightObj.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, 0.85f, 0.4f);
        glow.intensity = 1.5f;
        glow.range = 3f;
        glow.shadows = LightShadows.None;
    }

    void CreatePart(PrimitiveType type, string partName, Vector3 localPosition, Vector3 scale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = partName;
        part.transform.SetParent(visuals, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().material.color = color;
        Destroy(part.GetComponent<Collider>());
    }

    void Update()
    {
        if (collected) return;

        // Spin and bob
        visuals.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        visuals.localPosition = new Vector3(0f, Mathf.Sin(Time.time * bobSpeed) * bobHeight, 0f);

        CheckLookAtKey();

        Keyboard keyboard = Keyboard.current;
        if (isLookedAt && keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            Collect();
        }
    }

    void CheckLookAtKey()
    {
        isLookedAt = false;

        Camera cam = Camera.main;
        if (cam == null) return;

        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            isLookedAt = hit.collider.transform.IsChildOf(transform);
        }
    }

    void Collect()
    {
        collected = true;
        isLookedAt = false;
        HasKey = true;

        visuals.gameObject.SetActive(false);
        GetComponent<BoxCollider>().enabled = false;

        ShowMessage("Key collected");
    }

    void OnGUI()
    {
        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label);
            promptStyle.alignment = TextAnchor.MiddleCenter;
            promptStyle.fontSize = 24;
            promptStyle.normal.textColor = Color.white;

            messageStyle = new GUIStyle(GUI.skin.label);
            messageStyle.alignment = TextAnchor.MiddleCenter;
            messageStyle.fontSize = 28;
            messageStyle.fontStyle = FontStyle.Bold;
            messageStyle.normal.textColor = new Color(1f, 0.85f, 0.4f);
        }

        if (isLookedAt && !collected)
        {
            GUI.Label(new Rect(0f, Screen.height * 0.6f, Screen.width, 40f), "Press E to pick up the key", promptStyle);
        }

        if (Time.time < messageEndTime)
        {
            GUI.Label(new Rect(0f, Screen.height * 0.7f, Screen.width, 40f), messageText, messageStyle);
        }
    }
}