using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Spawn")]
    public Vector3 spawnPosition = new Vector3(0f, 0f, -2f);

    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 6f;
    public float jumpHeight = 1.1f;
    public float gravity = -20f;

    [Header("Mouse Look")]
    public float mouseSensitivity = 0.12f;
    public float maxLookAngle = 85f;

    [Header("Body")]
    public float playerHeight = 1.8f;
    public float playerRadius = 0.3f;

    [Header("Camera")]
    public float cameraHeight = 1.4f;

    private CharacterController controller;
    private Transform cameraTransform;
    private Camera playerCamera;

    private float verticalVelocity;
    private float pitch;
    private bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreatePlayer()
    {
        if (FindFirstObjectByType<PlayerController>() != null)
            return;

        GameObject playerObj = new GameObject("Player");
        playerObj.AddComponent<PlayerController>();
    }

    private void Awake()
    {
        // Force the lowered eye height. cameraHeight is serialized, so a value
        // saved on the Player object in the scene would otherwise override the
        // default above and keep the camera too high.
        cameraHeight = 1.4f;

        controller = GetComponent<CharacterController>();

        ConfigureCharacterController();
        CreateDedicatedPlayerCamera();

        ResetPlayerImmediate();
        initialized = true;
    }

    private void OnEnable()
    {
        if (!initialized || controller == null)
            return;

        verticalVelocity = -2f;
        pitch = 0f;

        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.identity;
    }

    private void Update()
    {
        if (!enabled || controller == null)
            return;

        HandleCursor();

        if (Cursor.lockState != CursorLockMode.Locked)
            return;

        HandleLook();
        HandleMovement();
    }

    private void ConfigureCharacterController()
    {
        controller.height = playerHeight;
        controller.radius = playerRadius;
        controller.center = new Vector3(0f, playerHeight * 0.5f, 0f);

        controller.minMoveDistance = 0f;
        controller.stepOffset = 0.3f;
        controller.slopeLimit = 45f;
        controller.skinWidth = 0.08f;
        controller.detectCollisions = true;
    }

    private void CreateDedicatedPlayerCamera()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);

        foreach (Camera cam in cameras)
        {
            if (cam == null)
                continue;

            if (cam.transform.IsChildOf(transform))
            {
                cam.enabled = false;
                cam.gameObject.tag = "Untagged";
            }
            else
            {
                cam.enabled = false;

                if (cam.CompareTag("MainCamera"))
                    cam.gameObject.tag = "Untagged";
            }
        }

        GameObject cameraObject = new GameObject("Player Camera");
        cameraObject.transform.SetParent(transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, cameraHeight, 0f);
        cameraObject.transform.localRotation = Quaternion.identity;

        playerCamera = cameraObject.AddComponent<Camera>();
        playerCamera.tag = "MainCamera";
        playerCamera.nearClipPlane = 0.03f;
        playerCamera.fieldOfView = 75f;
        playerCamera.enabled = true;

        AudioListener listener = cameraObject.AddComponent<AudioListener>();
        listener.enabled = true;

        cameraTransform = cameraObject.transform;
    }

    private void HandleCursor()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (mouse != null &&
            mouse.leftButton.wasPressedThisFrame &&
            Cursor.lockState != CursorLockMode.Locked)
        {
            LockCursor();
        }
    }

    private void HandleLook()
    {
        Mouse mouse = Mouse.current;

        if (mouse == null || cameraTransform == null)
            return;

        Vector2 mouseDelta = mouse.delta.ReadValue();

        float yaw = mouseDelta.x * mouseSensitivity;
        transform.Rotate(0f, yaw, 0f, Space.Self);

        pitch -= mouseDelta.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, -maxLookAngle, maxLookAngle);

        cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void HandleMovement()
    {
        Keyboard keyboard = Keyboard.current;

        if (keyboard == null || !controller.enabled)
            return;

        Vector2 input = Vector2.zero;

        if (keyboard.wKey.isPressed) input.y += 1f;
        if (keyboard.sKey.isPressed) input.y -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;

        input = Vector2.ClampMagnitude(input, 1f);

        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        Vector3 move = (forward * input.y) + (right * input.x);
        move.y = 0f;

        float speed = keyboard.leftShiftKey.isPressed
            ? sprintSpeed
            : walkSpeed;

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }
        else
        {
            verticalVelocity += gravity * Time.deltaTime;
        }

        if (keyboard.spaceKey.wasPressedThisFrame && controller.isGrounded)
        {
            verticalVelocity =
                Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        Vector3 velocity = move * speed;
        velocity.y = verticalVelocity;

        controller.Move(velocity * Time.deltaTime);
    }

    public void ResetPlayerImmediate()
    {
        if (controller == null)
            return;

        bool wasEnabled = controller.enabled;
        controller.enabled = false;

        transform.position = new Vector3(
            spawnPosition.x,
            0f,
            spawnPosition.z
        );

        transform.rotation = Quaternion.identity;

        verticalVelocity = -2f;
        pitch = 0f;

        if (cameraTransform != null)
        {
            cameraTransform.localPosition =
                new Vector3(0f, cameraHeight, 0f);

            cameraTransform.localRotation =
                Quaternion.identity;
        }

        controller.enabled = wasEnabled;
    }

    private void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}