using UnityEngine;
using UnityEngine.InputSystem;

public class DoorInteraction : MonoBehaviour
{
    public float openAngle = 90f;
    public float openSpeed = 4f;
    public float interactDistance = 3f;

    private float currentAngle;
    private float targetAngle;
    private bool isOpen;
    private bool isLookedAt;
    private GUIStyle promptStyle;

    // Runs automatically after the scene loads and builds the door in the front wall opening
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateDoor()
    {
        if (FindFirstObjectByType<DoorInteraction>() != null) return;

        float depth = 8f;
        float thickness = 0.2f;
        float doorWidth = 2f;
        float doorHeight = 2.5f;

        RoomBuilder room = FindFirstObjectByType<RoomBuilder>();
        if (room != null)
        {
            depth = room.depth;
            thickness = room.thickness;
            doorWidth = room.doorWidth;
            doorHeight = room.doorHeight;
        }

        float gap = 0.02f;
        float panelWidth = doorWidth - gap;
        float panelHeight = doorHeight - gap;
        float panelThickness = thickness * 0.5f;

        // Pivot sits on the hinge edge of the opening
        GameObject pivot = new GameObject("Door");
        pivot.transform.position = new Vector3(-doorWidth / 2f + gap / 2f, 0f, -(depth / 2f + thickness / 2f));
        pivot.AddComponent<DoorInteraction>();

        // Door panel
        CreateCube("Door Panel", pivot.transform,
            new Vector3(panelWidth / 2f, panelHeight / 2f, 0f),
            new Vector3(panelWidth, panelHeight, panelThickness),
            new Color(0.25f, 0.14f, 0.07f), true);

        // Door handle (visual only)
        CreateCube("Door Handle", pivot.transform,
            new Vector3(panelWidth - 0.25f, 1.1f, 0f),
            new Vector3(0.08f, 0.2f, panelThickness + 0.1f),
            new Color(0.6f, 0.6f, 0.6f), false);
    }

    private static void CreateCube(string cubeName, Transform parent, Vector3 localPosition, Vector3 scale, Color color, bool keepCollider)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = cubeName;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPosition;
        cube.transform.localScale = scale;
        cube.GetComponent<Renderer>().material.color = color;

        if (!keepCollider)
        {
            Destroy(cube.GetComponent<Collider>());
        }
    }

    void Update()
    {
        CheckLookAtDoor();

        Keyboard keyboard = Keyboard.current;
        if (isLookedAt && keyboard != null && keyboard.eKey.wasPressedThisFrame)
        {
            if (!isOpen && !KeyPickup.HasKey)
            {
                KeyPickup.ShowMessage("You need the key");
            }
            else
            {
                isOpen = !isOpen;
                targetAngle = isOpen ? openAngle : 0f;
            }
        }

        // Smooth animation
        currentAngle = Mathf.Lerp(currentAngle, targetAngle, 1f - Mathf.Exp(-openSpeed * Time.deltaTime));
        if (Mathf.Abs(currentAngle - targetAngle) < 0.05f)
        {
            currentAngle = targetAngle;
        }

        transform.localRotation = Quaternion.Euler(0f, currentAngle, 0f);
    }

    void CheckLookAtDoor()
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

    void OnGUI()
    {
        if (!isLookedAt) return;

        if (promptStyle == null)
        {
            promptStyle = new GUIStyle(GUI.skin.label);
            promptStyle.alignment = TextAnchor.MiddleCenter;
            promptStyle.fontSize = 24;
            promptStyle.normal.textColor = Color.white;
        }

        string message = isOpen ? "Press E to close" : "Press E to open";
        GUI.Label(new Rect(0f, Screen.height * 0.6f, Screen.width, 40f), message, promptStyle);
    }
}