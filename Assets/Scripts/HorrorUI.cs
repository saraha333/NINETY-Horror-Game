using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Symbols used by the puzzle. Shared with GameManager.cs.
public enum PuzzleSymbol
{
    Key,
    Box,
    Door
}

// HorrorUI.cs - NINETY
// Contains ONLY presentation: main menu, HUD, timer display, prompts, phone panel, note,
// puzzle interface, ending screens, fades, vignette and typography.
// It never touches the player, the timer logic or the game state.
// It reports button clicks to GameManager through the events below.
public class HorrorUI : MonoBehaviour
{
    // ======================================================================
    // EVENTS (UI -> GameManager)
    // ======================================================================
    public System.Action StartPressed;
    public System.Action PlayAgainPressed;
    public System.Action PuzzleResetPressed;
    public System.Action PuzzleClosePressed;
    public System.Action<PuzzleSymbol> SymbolPressed;

    // ======================================================================
    // PALETTE
    // ======================================================================
    private static readonly Color OffWhite = new Color(0.90f, 0.87f, 0.80f, 1f);
    private static readonly Color TextIdle = new Color(0.78f, 0.75f, 0.69f, 1f);
    private static readonly Color IconIdle = new Color(0.80f, 0.77f, 0.70f, 0.9f);
    private static readonly Color Grey = new Color(0.55f, 0.53f, 0.50f, 1f);
    private static readonly Color GreyDark = new Color(0.32f, 0.31f, 0.30f, 1f);
    private static readonly Color MutedRed = new Color(0.55f, 0.09f, 0.09f, 1f);
    private static readonly Color RedText = new Color(0.68f, 0.16f, 0.15f, 1f);
    private static readonly Color PanelDark = new Color(0.035f, 0.035f, 0.04f, 0.97f);
    private static readonly Color BorderColor = new Color(0.90f, 0.87f, 0.80f, 0.28f);
    private static readonly Color BorderHover = new Color(0.62f, 0.12f, 0.11f, 1f);
    private static readonly Color FillColor = new Color(0.05f, 0.05f, 0.055f, 0.92f);
    private static readonly Color FillHover = new Color(0.16f, 0.05f, 0.05f, 0.96f);

    // ======================================================================
    // PRIVATE TYPES
    // ======================================================================
    private class Panel
    {
        public RectTransform rect;
        public CanvasGroup group;
        public float target;
        public float speed = 6f;

        public bool Clickable
        {
            get { return target > 0.5f && group.alpha > 0.5f && rect.gameObject.activeInHierarchy; }
        }
    }

    // Text with letter spacing (one Text per letter inside a layout group)
    private class Tracked
    {
        public RectTransform rect;
        public List<Text> letters = new List<Text>();

        public void SetColor(Color c)
        {
            for (int i = 0; i < letters.Count; i++) letters[i].color = c;
        }
    }

    private class UIButton
    {
        public Panel owner;
        public RectTransform rect;
        public Image border;
        public Image fill;
        public Tracked label;
        public List<Image> icon = new List<Image>();
        public System.Action onClick;
        public bool disabled;
        public float hover;
    }

    // ======================================================================
    // FIELDS
    // ======================================================================
    private readonly List<Panel> panels = new List<Panel>();
    private readonly List<UIButton> buttons = new List<UIButton>();

    private Font font;
    private RectTransform canvasRoot;
    private Sprite discSprite;
    private Sprite ringSprite;
    private Sprite vignetteSprite;
    private Sprite gradientSprite;

    private Panel menu;
    private Panel hud;
    private Panel timerPanel;
    private Panel prompt;
    private Panel phone;
    private Panel clue;
    private Panel notice;
    private Panel puzzle;
    private Panel ending;

    private Image vignette;
    private Image fadeImage;
    private Image timerBar;
    private Image crosshair;
    private Image objectiveAccent;

    private Text objectiveText;
    private Text timerText;
    private Text promptText;
    private Text phoneSpeakerText;
    private Text phoneLineText;
    private Text noticeText;
    private Text puzzleMessage;
    private Text endMessage;

    private CanvasGroup objectiveGroup;
    private CanvasGroup menuTitleGroup;
    private Tracked endGameOverTitle;
    private Tracked endEscapedTitle;

    private RectTransform puzzleCard;
    private readonly List<RectTransform> slotContent = new List<RectTransform>();
    private readonly List<Image> slotBorders = new List<Image>();
    private readonly UIButton[] symbolButtons = new UIButton[3];
    private readonly List<RectTransform> phoneBars = new List<RectTransform>();

    private float timerRemaining;
    private float timerTotal = 90f;
    private bool timerVisible;
    private float vignetteStrength = 0.8f;
    private float vignetteTarget = 0.8f;
    private float objectiveFade = 1f;
    private float noticeTimer;
    private float clueTimer;
    private float puzzleShake;
    private string phoneFull = "";
    private float phoneChars;
    private float fadeAlpha = 1f;
    private float fadeSpeed;
    private string currentPrompt = "";

    // ======================================================================
    // UNITY
    // ======================================================================
    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);

        discSprite = MakeCircleSprite(false);
        ringSprite = MakeCircleSprite(true);
        vignetteSprite = MakeVignetteSprite();
        gradientSprite = MakeGradientSprite();

        BuildCanvas();
        BuildVignette();
        BuildHud();
        BuildPhonePanel();
        BuildCluePanel();
        BuildNotice();
        BuildPuzzle();
        BuildEnding();
        BuildMenu();
        BuildFade();
    }

    void Update()
    {
        float dt = Time.deltaTime;

        UpdatePanels(dt);
        UpdateButtons(dt);
        UpdateObjective(dt);
        UpdateTimerVisuals(dt);
        UpdatePhone(dt);
        UpdateTimedPanels(dt);
        UpdateMisc(dt);
    }

    // ======================================================================
    // PUBLIC API (GameManager -> UI)
    // ======================================================================
    public void ShowMainMenu()
    {
        ResetForNewRun();
        SetPanel(hud, false, true, 6f);
        SetPanel(menu, true, true, 6f);
        fadeAlpha = 0f;
        fadeSpeed = 0f;
        fadeImage.color = new Color(0f, 0f, 0f, 0f);
        fadeImage.gameObject.SetActive(false);
    }

    public void HideMainMenu()
    {
        SetPanel(menu, false, false, 4f);
        FadeFromBlack(1.4f);
    }

    public void ShowHud(bool visible)
    {
        SetPanel(hud, visible, false, 4f);
    }

    public void ShowTimer(bool visible)
    {
        timerVisible = visible;
        SetPanel(timerPanel, visible, false, 4f);
    }

    public void SetObjective(string text)
    {
        if (objectiveText.text == text) return;
        objectiveText.text = text;
        objectiveFade = 0f;
    }

    public void SetTimer(float secondsRemaining, float totalSeconds)
    {
        timerRemaining = secondsRemaining;
        timerTotal = totalSeconds;

        int secs = Mathf.CeilToInt(Mathf.Max(0f, secondsRemaining));
        timerText.text = string.Format("{0:00}:{1:00}", secs / 60, secs % 60);
    }

    public void SetPrompt(string action)
    {
        if (string.IsNullOrEmpty(action))
        {
            SetPanel(prompt, false, false, 10f);
            return;
        }

        if (action != currentPrompt)
        {
            currentPrompt = action;
            promptText.text = "\u2022  " + action;
        }

        SetPanel(prompt, true, false, 10f);
    }

    public void ShowPhoneCall(string speaker, string line)
    {
        phoneSpeakerText.text = speaker;
        phoneFull = "\u201C" + line + "\u201D";
        phoneChars = 0f;
        phoneLineText.text = "";
        SetPanel(phone, true, false, 5f);
    }

    public void HidePhoneCall()
    {
        SetPanel(phone, false, false, 5f);
    }

    public void ShowEnvelopeClue(float seconds)
    {
        clueTimer = seconds;
        SetPanel(clue, true, false, 5f);
    }

    public void HideEnvelopeClue()
    {
        clueTimer = 0f;
        SetPanel(clue, false, false, 8f);
    }

    public void ShowNotice(string text, float seconds)
    {
        noticeText.text = text;
        noticeTimer = seconds;
        SetPanel(notice, true, false, 6f);
    }

    public void ShowPuzzle()
    {
        puzzleMessage.text = "";
        SetPuzzleSelection(new List<PuzzleSymbol>());
        SetPanel(puzzle, true, false, 8f);
    }

    public void HidePuzzle()
    {
        SetPanel(puzzle, false, false, 8f);
    }

    public void SetPuzzleSelection(IList<PuzzleSymbol> selection)
    {
        for (int i = 0; i < slotContent.Count; i++)
        {
            RectTransform content = slotContent[i];
            for (int c = content.childCount - 1; c >= 0; c--)
            {
                Destroy(content.GetChild(c).gameObject);
            }

            if (i < selection.Count)
            {
                BuildIcon(content, selection[i], 60f, OffWhite, null);
            }

            slotBorders[i].color = (i == selection.Count) ? BorderHover : BorderColor;
        }

        for (int s = 0; s < symbolButtons.Length; s++)
        {
            if (symbolButtons[s] != null)
            {
                symbolButtons[s].disabled = selection.Contains((PuzzleSymbol)s);
            }
        }
    }

    public void ShowPuzzleError()
    {
        puzzleMessage.text = "INCORRECT SEQUENCE";
        puzzleMessage.color = RedText;
        puzzleShake = 0.45f;
    }

    public void ShowPuzzleSuccess()
    {
        puzzleMessage.text = "SEQUENCE ACCEPTED";
        puzzleMessage.color = OffWhite;
    }

    public void ClearPuzzleMessage()
    {
        puzzleMessage.text = "";
    }

    public void ShowEnding(bool escaped, string message)
    {
        endGameOverTitle.rect.gameObject.SetActive(!escaped);
        endEscapedTitle.rect.gameObject.SetActive(escaped);
        endMessage.text = message;
        endMessage.color = escaped ? OffWhite : TextIdle;

        SetPanel(hud, false, false, 6f);
        SetPanel(prompt, false, true, 10f);
        SetPanel(phone, false, false, 8f);
        SetPanel(clue, false, false, 8f);
        SetPanel(puzzle, false, false, 8f);
        SetPanel(notice, false, false, 8f);
        SetPanel(ending, true, false, 1.8f);
    }

    // Clears every transient panel before a new run (does not touch the main menu or fade)
    public void ResetForNewRun()
    {
        SetPanel(puzzle, false, true, 8f);
        SetPanel(clue, false, true, 8f);
        SetPanel(phone, false, true, 8f);
        SetPanel(notice, false, true, 8f);
        SetPanel(ending, false, true, 8f);
        SetPanel(prompt, false, true, 10f);
        SetPanel(timerPanel, false, true, 8f);

        noticeTimer = 0f;
        clueTimer = 0f;
        puzzleShake = 0f;
        puzzleMessage.text = "";
        currentPrompt = "";
        timerVisible = false;
        timerRemaining = timerTotal;
        objectiveText.text = "";
        vignetteTarget = 0.8f;
    }

    public void FadeFromBlack(float duration)
    {
        fadeAlpha = 1f;
        fadeSpeed = 1f / Mathf.Max(duration, 0.05f);
        fadeImage.color = new Color(0f, 0f, 0f, 1f);
        fadeImage.gameObject.SetActive(true);
    }

    // ======================================================================
    // UPDATE HELPERS
    // ======================================================================
    private void UpdatePanels(float dt)
    {
        for (int i = 0; i < panels.Count; i++)
        {
            Panel p = panels[i];

            if (!Mathf.Approximately(p.group.alpha, p.target))
            {
                p.group.alpha = Mathf.MoveTowards(p.group.alpha, p.target, p.speed * dt);
            }

            if (p.target <= 0f && p.group.alpha <= 0.001f && p.rect.gameObject.activeSelf)
            {
                p.rect.gameObject.SetActive(false);
            }
        }
    }

    // Buttons are hit-tested manually, so no EventSystem or input module is needed.
    private void UpdateButtons(float dt)
    {
        Mouse mouse = Mouse.current;
        Vector2 mousePos = mouse != null ? mouse.position.ReadValue() : new Vector2(-1000f, -1000f);
        bool pressed = mouse != null && mouse.leftButton.wasPressedThisFrame;

        for (int i = 0; i < buttons.Count; i++)
        {
            UIButton b = buttons[i];
            if (!b.owner.rect.gameObject.activeInHierarchy) continue;

            bool clickable = b.owner.Clickable && !b.disabled;
            bool over = clickable && RectTransformUtility.RectangleContainsScreenPoint(b.rect, mousePos, null);

            b.hover = Mathf.MoveTowards(b.hover, over ? 1f : 0f, dt * 8f);
            ApplyButtonVisual(b);

            if (over && pressed && b.onClick != null)
            {
                b.onClick();
            }
        }
    }

    private void ApplyButtonVisual(UIButton b)
    {
        Color border;
        Color fill;
        Color text;
        Color icon;

        if (b.disabled)
        {
            border = new Color(0.9f, 0.87f, 0.8f, 0.1f);
            fill = new Color(0.03f, 0.03f, 0.035f, 0.9f);
            text = GreyDark;
            icon = new Color(OffWhite.r, OffWhite.g, OffWhite.b, 0.2f);
        }
        else
        {
            border = Color.Lerp(BorderColor, BorderHover, b.hover);
            fill = Color.Lerp(FillColor, FillHover, b.hover);
            text = Color.Lerp(TextIdle, OffWhite, b.hover);
            icon = Color.Lerp(IconIdle, OffWhite, b.hover);
        }

        b.border.color = border;
        b.fill.color = fill;
        if (b.label != null) b.label.SetColor(text);
        for (int i = 0; i < b.icon.Count; i++) b.icon[i].color = icon;
    }

    private void UpdateObjective(float dt)
    {
        objectiveFade = Mathf.MoveTowards(objectiveFade, 1f, dt * 1.5f);
        objectiveGroup.alpha = objectiveFade;
        objectiveAccent.color = Color.Lerp(RedText, MutedRed, objectiveFade);
    }

    private void UpdateTimerVisuals(float dt)
    {
        float rem = timerRemaining;
        Color c = OffWhite;
        float scale = 1f;

        if (rem <= 30f)
        {
            c = Color.Lerp(OffWhite, new Color(0.78f, 0.16f, 0.14f), Mathf.InverseLerp(30f, 10f, rem));
        }

        if (rem <= 15f && rem > 0f)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 7f);
            c.a = Mathf.Lerp(0.6f, 1f, pulse);
            scale = 1f + 0.05f * pulse;
        }

        timerText.color = c;
        timerText.rectTransform.localScale = new Vector3(scale, scale, 1f);

        float frac = timerTotal > 0f ? Mathf.Clamp01(rem / timerTotal) : 0f;
        timerBar.rectTransform.sizeDelta = new Vector2(180f * frac, 2f);
        timerBar.color = new Color(c.r, c.g, c.b, 0.9f);

        vignetteTarget = (timerVisible && rem <= 15f) ? 0.95f : 0.8f;
    }

    private void UpdatePhone(float dt)
    {
        if (!phone.rect.gameObject.activeInHierarchy) return;

        if (phone.target > 0.5f && phoneChars < phoneFull.Length)
        {
            phoneChars += 38f * dt;
            int n = Mathf.Min(phoneFull.Length, Mathf.FloorToInt(phoneChars));
            phoneLineText.text = phoneFull.Substring(0, n);
        }

        bool talking = phone.target > 0.5f;
        for (int i = 0; i < phoneBars.Count; i++)
        {
            float h = 4f;
            if (talking)
            {
                float a = 0.5f + 0.5f * Mathf.Sin(Time.time * 9f + i * 1.4f);
                float b = 0.5f + 0.5f * Mathf.Sin(Time.time * 3.1f + i * 0.7f);
                h = 8f + 30f * a * b;
            }
            phoneBars[i].sizeDelta = new Vector2(4f, h);
        }
    }

    private void UpdateTimedPanels(float dt)
    {
        if (noticeTimer > 0f)
        {
            noticeTimer -= dt;
            if (noticeTimer <= 0f) SetPanel(notice, false, false, 6f);
        }

        if (clueTimer > 0f)
        {
            clueTimer -= dt;
            if (clueTimer <= 0f) SetPanel(clue, false, false, 5f);
        }
    }

    private void UpdateMisc(float dt)
    {
        // Fade overlay
        if (fadeImage.gameObject.activeSelf)
        {
            fadeAlpha = Mathf.MoveTowards(fadeAlpha, 0f, fadeSpeed * dt);
            fadeImage.color = new Color(0f, 0f, 0f, fadeAlpha);
            if (fadeAlpha <= 0.001f) fadeImage.gameObject.SetActive(false);
        }

        // Vignette
        vignetteStrength = Mathf.MoveTowards(vignetteStrength, vignetteTarget, dt * 0.5f);
        vignette.color = new Color(1f, 1f, 1f, vignetteStrength);

        // Crosshair brightens when something can be used
        float ca = crosshair.color.a;
        float want = prompt.target > 0.5f ? 0.95f : 0.4f;
        ca = Mathf.MoveTowards(ca, want, dt * 4f);
        crosshair.color = new Color(OffWhite.r, OffWhite.g, OffWhite.b, ca);

        // Puzzle card shake on a wrong sequence
        if (puzzleShake > 0f)
        {
            puzzleShake -= dt;
            float amount = Mathf.Max(0f, puzzleShake) / 0.45f;
            puzzleCard.anchoredPosition = new Vector2(Mathf.Sin(Time.time * 60f) * 8f * amount, 0f);
        }
        else if (puzzleCard.anchoredPosition.x != 0f)
        {
            puzzleCard.anchoredPosition = Vector2.zero;
        }

        // Subtle flicker of the menu title
        if (menu.target > 0.5f && menuTitleGroup != null)
        {
            float n = Mathf.PerlinNoise(Time.time * 1.4f, 7.7f);
            menuTitleGroup.alpha = 1f - 0.45f * Mathf.Clamp01((n - 0.72f) * 8f);
        }
    }

    private void SetPanel(Panel p, bool visible, bool instant, float speed)
    {
        p.target = visible ? 1f : 0f;
        p.speed = speed;

        if (visible && !p.rect.gameObject.activeSelf) p.rect.gameObject.SetActive(true);

        if (instant)
        {
            p.group.alpha = p.target;
            if (!visible) p.rect.gameObject.SetActive(false);
        }
    }

    // ======================================================================
    // BUILDERS
    // ======================================================================
    private void BuildCanvas()
    {
        GameObject canvasObj = new GameObject("Horror UI Canvas");
        canvasObj.transform.SetParent(transform, false);

        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasRoot = canvasObj.GetComponent<RectTransform>();
    }

    private void BuildVignette()
    {
        vignette = NewImage("Vignette", canvasRoot, new Color(1f, 1f, 1f, 0.8f));
        vignette.sprite = vignetteSprite;
        Stretch(vignette.rectTransform);
    }

    private void BuildHud()
    {
        hud = NewPanel("HUD", canvasRoot, true);

        // Objective (top-left)
        RectTransform obj = NewRect("Objective", hud.rect);
        Place(obj, 0f, 1f, 0f, 1f, 56f, -48f, 900f, 80f);
        objectiveGroup = obj.gameObject.AddComponent<CanvasGroup>();

        objectiveAccent = NewImage("Accent", obj, MutedRed);
        Place(objectiveAccent.rectTransform, 0f, 1f, 0f, 1f, 0f, -2f, 3f, 62f);

        Tracked objLabel = MakeTracked(obj, "Label", "OBJECTIVE", 15, RedText, FontStyle.Bold, 5f);
        Place(objLabel.rect, 0f, 1f, 0f, 1f, 16f, 0f, 0f, 0f);

        objectiveText = NewText("Text", obj, "", 30, OffWhite, TextAnchor.UpperLeft, FontStyle.Normal, true);
        Place(objectiveText.rectTransform, 0f, 1f, 0f, 1f, 16f, -24f, 860f, 44f);

        // Timer (top-center)
        timerPanel = NewPanel("Timer", hud.rect, false);
        Place(timerPanel.rect, 0.5f, 1f, 0.5f, 1f, 0f, -36f, 300f, 100f);

        timerText = NewText("Digits", timerPanel.rect, "01:30", 58, OffWhite, TextAnchor.MiddleCenter, FontStyle.Bold, true);
        Place(timerText.rectTransform, 0.5f, 1f, 0.5f, 1f, 0f, 0f, 300f, 72f);

        Image barBg = NewImage("BarBg", timerPanel.rect, new Color(1f, 1f, 1f, 0.12f));
        Place(barBg.rectTransform, 0.5f, 1f, 0.5f, 1f, 0f, -80f, 180f, 2f);

        timerBar = NewImage("BarFill", timerPanel.rect, OffWhite);
        Place(timerBar.rectTransform, 0.5f, 1f, 0f, 1f, -90f, -80f, 180f, 2f);

        // Crosshair
        crosshair = NewImage("Crosshair", hud.rect, new Color(OffWhite.r, OffWhite.g, OffWhite.b, 0.4f));
        crosshair.sprite = discSprite;
        Place(crosshair.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f, 5f, 5f);

        // Interaction prompt: [E]  •  ACTION
        prompt = NewPanel("Prompt", hud.rect, false);
        Place(prompt.rect, 0.5f, 0.3f, 0.5f, 0.5f, 0f, 0f, 700f, 70f);

        HorizontalLayoutGroup row = prompt.rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 14f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        RectTransform cap = NewRect("KeyCap", prompt.rect);
        LayoutElement capLayout = cap.gameObject.AddComponent<LayoutElement>();
        capLayout.minWidth = 40f;
        capLayout.minHeight = 40f;
        capLayout.preferredWidth = 40f;
        capLayout.preferredHeight = 40f;
        Image capBorder = cap.gameObject.AddComponent<Image>();
        capBorder.color = new Color(OffWhite.r, OffWhite.g, OffWhite.b, 0.85f);
        capBorder.raycastTarget = false;

        Image capFill = NewImage("Fill", cap, new Color(0.04f, 0.04f, 0.05f, 0.95f));
        StretchInset(capFill.rectTransform, 2f);

        Text capText = NewText("E", cap, "E", 22, OffWhite, TextAnchor.MiddleCenter, FontStyle.Bold, false);
        Stretch(capText.rectTransform);

        promptText = NewText("Action", prompt.rect, "", 26, OffWhite, TextAnchor.MiddleCenter, FontStyle.Normal, true);
        promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
        promptText.verticalOverflow = VerticalWrapMode.Overflow;
    }

    private void BuildPhonePanel()
    {
        phone = NewPanel("Phone Call", canvasRoot, false);
        Place(phone.rect, 0.5f, 0f, 0.5f, 0f, 0f, 90f, 880f, 150f);

        Image bg = NewImage("Bg", phone.rect, new Color(0.03f, 0.03f, 0.04f, 0.9f));
        Stretch(bg.rectTransform);

        Image topLine = NewImage("TopLine", phone.rect, new Color(OffWhite.r, OffWhite.g, OffWhite.b, 0.22f));
        topLine.rectTransform.anchorMin = new Vector2(0f, 1f);
        topLine.rectTransform.anchorMax = new Vector2(1f, 1f);
        topLine.rectTransform.pivot = new Vector2(0.5f, 1f);
        topLine.rectTransform.anchoredPosition = Vector2.zero;
        topLine.rectTransform.sizeDelta = new Vector2(0f, 1f);

        Image accent = NewImage("Accent", phone.rect, MutedRed);
        accent.rectTransform.anchorMin = new Vector2(0f, 0f);
        accent.rectTransform.anchorMax = new Vector2(0f, 1f);
        accent.rectTransform.pivot = new Vector2(0f, 0.5f);
        accent.rectTransform.anchoredPosition = Vector2.zero;
        accent.rectTransform.sizeDelta = new Vector2(3f, 0f);

        Tracked header = MakeTracked(phone.rect, "Header", "INCOMING CALL", 14, RedText, FontStyle.Bold, 5f);
        Place(header.rect, 0f, 1f, 0f, 1f, 30f, -18f, 0f, 0f);

        phoneSpeakerText = NewText("Speaker", phone.rect, "", 22, OffWhite, TextAnchor.UpperLeft, FontStyle.Bold, true);
        Place(phoneSpeakerText.rectTransform, 0f, 1f, 0f, 1f, 30f, -42f, 600f, 30f);

        phoneLineText = NewText("Line", phone.rect, "", 32, OffWhite, TextAnchor.UpperLeft, FontStyle.Italic, true);
        Place(phoneLineText.rectTransform, 0f, 1f, 0f, 1f, 30f, -78f, 700f, 56f);

        // Voice bars
        RectTransform barsRoot = NewRect("Bars", phone.rect);
        Place(barsRoot, 1f, 0.5f, 1f, 0.5f, -50f, 0f, 120f, 60f);
        for (int i = 0; i < 9; i++)
        {
            Image bar = NewImage("Bar", barsRoot, new Color(OffWhite.r, OffWhite.g, OffWhite.b, 0.55f));
            Place(bar.rectTransform, 0f, 0.5f, 0.5f, 0.5f, 6f + i * 13f, 0f, 4f, 4f);
            phoneBars.Add(bar.rectTransform);
        }
    }

    private void BuildCluePanel()
    {
        clue = NewPanel("Clue", canvasRoot, false);
        Place(clue.rect, 0.5f, 0.62f, 0.5f, 0.5f, 0f, 0f, 820f, 300f);

        Image border = NewImage("Border", clue.rect, BorderColor);
        Stretch(border.rectTransform);
        Image bg = NewImage("Bg", clue.rect, new Color(0.045f, 0.04f, 0.04f, 0.95f));
        StretchInset(bg.rectTransform, 1f);

        Tracked header = MakeTracked(clue.rect, "Header", "THE NOTE", 16, Grey, FontStyle.Bold, 6f);
        Place(header.rect, 0.5f, 1f, 0.5f, 1f, 0f, -26f, 0f, 0f);

        float[] xs = { -250f, 0f, 250f };
        PuzzleSymbol[] symbols = { PuzzleSymbol.Key, PuzzleSymbol.Box, PuzzleSymbol.Door };
        string[] names = { "KEY", "BOX", "DOOR" };

        for (int i = 0; i < 3; i++)
        {
            RectTransform iconRoot = NewRect("Icon", clue.rect);
            Place(iconRoot, 0.5f, 0.5f, 0.5f, 0.5f, xs[i], 18f, 120f, 120f);
            BuildIcon(iconRoot, symbols[i], 120f, OffWhite, null);

            Tracked label = MakeTracked(clue.rect, "Label", names[i], 20, OffWhite, FontStyle.Bold, 5f);
            Place(label.rect, 0.5f, 0.5f, 0.5f, 0.5f, xs[i], -62f, 0f, 0f);
        }

        Text dotA = NewText("DotA", clue.rect, "\u2022", 30, Grey, TextAnchor.MiddleCenter, FontStyle.Normal, false);
        Place(dotA.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f, -125f, 18f, 40f, 40f);
        Text dotB = NewText("DotB", clue.rect, "\u2022", 30, Grey, TextAnchor.MiddleCenter, FontStyle.Normal, false);
        Place(dotB.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f, 125f, 18f, 40f, 40f);

        Text footer = NewText("Footer", clue.rect, "Remember the order.", 16, Grey, TextAnchor.MiddleCenter, FontStyle.Italic, false);
        Place(footer.rectTransform, 0.5f, 0f, 0.5f, 0f, 0f, 22f, 600f, 24f);
    }

    private void BuildNotice()
    {
        notice = NewPanel("Notice", canvasRoot, false);
        Place(notice.rect, 0.5f, 0.22f, 0.5f, 0.5f, 0f, 0f, 900f, 50f);

        noticeText = NewText("Text", notice.rect, "", 24, OffWhite, TextAnchor.MiddleCenter, FontStyle.Normal, true);
        Stretch(noticeText.rectTransform);
    }

    private void BuildPuzzle()
    {
        puzzle = NewPanel("Puzzle", canvasRoot, true);

        Image dim = NewImage("Dim", puzzle.rect, new Color(0f, 0f, 0f, 0.8f));
        Stretch(dim.rectTransform);

        puzzleCard = NewRect("Card", puzzle.rect);
        Place(puzzleCard, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f, 940f, 680f);

        Image border = NewImage("Border", puzzleCard, BorderColor);
        Stretch(border.rectTransform);
        Image bg = NewImage("Bg", puzzleCard, PanelDark);
        StretchInset(bg.rectTransform, 1f);

        Tracked title = MakeTracked(puzzleCard, "Title", "SEQUENCE", 30, OffWhite, FontStyle.Bold, 10f);
        Place(title.rect, 0.5f, 1f, 0.5f, 1f, 0f, -46f, 0f, 0f);

        Text subtitle = NewText("Subtitle", puzzleCard, "Select the symbols in the correct order.", 20, Grey, TextAnchor.MiddleCenter, FontStyle.Normal, false);
        Place(subtitle.rectTransform, 0.5f, 1f, 0.5f, 1f, 0f, -100f, 700f, 30f);

        // Three slots that show the chosen sequence
        float[] slotX = { -130f, 0f, 130f };
        for (int i = 0; i < 3; i++)
        {
            RectTransform slot = NewRect("Slot", puzzleCard);
            Place(slot, 0.5f, 1f, 0.5f, 0.5f, slotX[i], -205f, 96f, 96f);

            Image slotBorder = NewImage("Border", slot, BorderColor);
            Stretch(slotBorder.rectTransform);
            slotBorders.Add(slotBorder);

            Image slotFill = NewImage("Fill", slot, FillColor);
            StretchInset(slotFill.rectTransform, 2f);

            Text number = NewText("Number", slot, (i + 1).ToString(), 14, GreyDark, TextAnchor.UpperLeft, FontStyle.Normal, false);
            Place(number.rectTransform, 0f, 1f, 0f, 1f, 7f, -4f, 20f, 18f);

            RectTransform content = NewRect("Content", slot);
            Place(content, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f, 60f, 60f);
            slotContent.Add(content);
        }

        // Three symbol buttons (shown as DOOR, KEY, BOX, so the player must remember the clue)
        PuzzleSymbol[] order = { PuzzleSymbol.Door, PuzzleSymbol.Key, PuzzleSymbol.Box };
        string[] names = { "DOOR", "KEY", "BOX" };
        float[] buttonX = { -260f, 0f, 260f };

        for (int i = 0; i < 3; i++)
        {
            PuzzleSymbol symbol = order[i];

            UIButton b = MakeButtonBase(puzzle, puzzleCard, "Symbol " + names[i]);
            Place(b.rect, 0.5f, 1f, 0.5f, 0.5f, buttonX[i], -400f, 190f, 190f);

            RectTransform iconRoot = NewRect("Icon", b.rect);
            Place(iconRoot, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 18f, 96f, 96f);
            BuildIcon(iconRoot, symbol, 96f, OffWhite, b.icon);

            b.label = MakeTracked(b.rect, "Label", names[i], 20, OffWhite, FontStyle.Bold, 5f);
            Place(b.label.rect, 0.5f, 0f, 0.5f, 0f, 0f, 20f, 0f, 0f);

            b.onClick = () => { if (SymbolPressed != null) SymbolPressed(symbol); };
            symbolButtons[(int)symbol] = b;
        }

        puzzleMessage = NewText("Message", puzzleCard, "", 22, RedText, TextAnchor.MiddleCenter, FontStyle.Bold, false);
        Place(puzzleMessage.rectTransform, 0.5f, 1f, 0.5f, 0.5f, 0f, -530f, 700f, 34f);

        UIButton reset = MakeButton(puzzle, puzzleCard, "Reset", "RESET", 18, 4f);
        Place(reset.rect, 0.5f, 1f, 0.5f, 0.5f, -110f, -610f, 180f, 48f);
        reset.onClick = () => { if (PuzzleResetPressed != null) PuzzleResetPressed(); };

        UIButton close = MakeButton(puzzle, puzzleCard, "Close", "CLOSE", 18, 4f);
        Place(close.rect, 0.5f, 1f, 0.5f, 0.5f, 110f, -610f, 180f, 48f);
        close.onClick = () => { if (PuzzleClosePressed != null) PuzzleClosePressed(); };
    }

    private void BuildEnding()
    {
        ending = NewPanel("Ending", canvasRoot, true);

        Image dim = NewImage("Dim", ending.rect, new Color(0f, 0f, 0f, 0.3f));
        Stretch(dim.rectTransform);

        Image gradient = NewImage("Gradient", ending.rect, Color.white);
        gradient.sprite = gradientSprite;
        gradient.rectTransform.anchorMin = new Vector2(0f, 0f);
        gradient.rectTransform.anchorMax = new Vector2(1f, 0f);
        gradient.rectTransform.pivot = new Vector2(0.5f, 0f);
        gradient.rectTransform.anchoredPosition = Vector2.zero;
        gradient.rectTransform.sizeDelta = new Vector2(0f, 560f);

        endGameOverTitle = MakeTracked(ending.rect, "Title Game Over", "GAME OVER", 92, RedText, FontStyle.Bold, 18f);
        Place(endGameOverTitle.rect, 0.5f, 0.25f, 0.5f, 0.5f, 0f, 0f, 0f, 0f);

        endEscapedTitle = MakeTracked(ending.rect, "Title Escaped", "YOU ESCAPED", 92, OffWhite, FontStyle.Bold, 18f);
        Place(endEscapedTitle.rect, 0.5f, 0.25f, 0.5f, 0.5f, 0f, 0f, 0f, 0f);

        endMessage = NewText("Message", ending.rect, "", 26, TextIdle, TextAnchor.MiddleCenter, FontStyle.Normal, true);
        Place(endMessage.rectTransform, 0.5f, 0.185f, 0.5f, 0.5f, 0f, 0f, 1100f, 40f);

        UIButton playAgain = MakeButton(ending, ending.rect, "Play Again", "PLAY AGAIN", 24, 4f);
        Place(playAgain.rect, 0.5f, 0.1f, 0.5f, 0.5f, 0f, 0f, 360f, 68f);
        playAgain.onClick = () => { if (PlayAgainPressed != null) PlayAgainPressed(); };
    }

    private void BuildMenu()
    {
        menu = NewPanel("Main Menu", canvasRoot, true);

        Image bg = NewImage("Bg", menu.rect, new Color(0.012f, 0.012f, 0.016f, 1f));
        Stretch(bg.rectTransform);

        Image menuVignette = NewImage("Vignette", menu.rect, Color.white);
        menuVignette.sprite = vignetteSprite;
        Stretch(menuVignette.rectTransform);

        Tracked title = MakeTracked(menu.rect, "Title", "NINETY", 150, OffWhite, FontStyle.Bold, 34f);
        Place(title.rect, 0.5f, 0.63f, 0.5f, 0.5f, 0f, 0f, 0f, 0f);
        menuTitleGroup = title.rect.gameObject.AddComponent<CanvasGroup>();

        Image line = NewImage("Line", menu.rect, MutedRed);
        Place(line.rectTransform, 0.5f, 0.52f, 0.5f, 0.5f, 0f, 0f, 420f, 2f);

        Text tagline = NewText("Tagline", menu.rect, "YOU HAVE NINETY SECONDS.", 20, Grey, TextAnchor.MiddleCenter, FontStyle.Normal, false);
        Place(tagline.rectTransform, 0.5f, 0.48f, 0.5f, 0.5f, 0f, 0f, 800f, 30f);

        UIButton start = MakeButton(menu, menu.rect, "Start", "START GAME", 26, 5f);
        Place(start.rect, 0.5f, 0.34f, 0.5f, 0.5f, 0f, 0f, 380f, 72f);
        start.onClick = () => { if (StartPressed != null) StartPressed(); };

        Text controls = NewText("Controls", menu.rect, "WASD  MOVE      MOUSE  LOOK      E  INTERACT      SHIFT  SPRINT      SPACE  JUMP", 16, GreyDark, TextAnchor.MiddleCenter, FontStyle.Normal, false);
        Place(controls.rectTransform, 0.5f, 0.06f, 0.5f, 0.5f, 0f, 0f, 1400f, 30f);
    }

    private void BuildFade()
    {
        fadeImage = NewImage("Fade", canvasRoot, new Color(0f, 0f, 0f, 1f));
        Stretch(fadeImage.rectTransform);
        fadeAlpha = 1f;
        fadeSpeed = 0f;
    }

    // ======================================================================
    // ICONS (drawn from simple UI shapes)
    // ======================================================================
    private void BuildIcon(RectTransform parent, PuzzleSymbol symbol, float s, Color color, List<Image> collect)
    {
        if (symbol == PuzzleSymbol.Key)
        {
            IconRect(parent, 0f, 0.27f * s, 0.40f * s, 0.40f * s, color, collect, ringSprite);
            IconRect(parent, 0f, -0.10f * s, 0.08f * s, 0.56f * s, color, collect, null);
            IconRect(parent, 0.085f * s, -0.30f * s, 0.13f * s, 0.07f * s, color, collect, null);
            IconRect(parent, 0.07f * s, -0.18f * s, 0.10f * s, 0.07f * s, color, collect, null);
        }
        else if (symbol == PuzzleSymbol.Box)
        {
            IconFrame(parent, 0f, -0.10f * s, 0.70f * s, 0.38f * s, 0.05f * s, color, collect);
            IconFrame(parent, 0f, 0.17f * s, 0.74f * s, 0.16f * s, 0.05f * s, color, collect);
            IconRect(parent, 0f, 0.03f * s, 0.09f * s, 0.12f * s, color, collect, null);
        }
        else
        {
            IconFrame(parent, 0f, 0f, 0.46f * s, 0.80f * s, 0.05f * s, color, collect);
            IconFrame(parent, 0f, 0.19f * s, 0.28f * s, 0.26f * s, 0.025f * s, color, collect);
            IconFrame(parent, 0f, -0.17f * s, 0.28f * s, 0.26f * s, 0.025f * s, color, collect);
            IconRect(parent, 0.13f * s, -0.02f * s, 0.07f * s, 0.07f * s, color, collect, discSprite);
        }
    }

    private void IconRect(RectTransform parent, float cx, float cy, float w, float h, Color color, List<Image> collect, Sprite sprite)
    {
        Image img = NewImage("Part", parent, color);
        img.sprite = sprite;
        Place(img.rectTransform, 0.5f, 0.5f, 0.5f, 0.5f, cx, cy, w, h);
        if (collect != null) collect.Add(img);
    }

    private void IconFrame(RectTransform parent, float cx, float cy, float w, float h, float t, Color color, List<Image> collect)
    {
        IconRect(parent, cx, cy + h / 2f - t / 2f, w, t, color, collect, null);
        IconRect(parent, cx, cy - h / 2f + t / 2f, w, t, color, collect, null);
        IconRect(parent, cx - w / 2f + t / 2f, cy, t, h - 2f * t, color, collect, null);
        IconRect(parent, cx + w / 2f - t / 2f, cy, t, h - 2f * t, color, collect, null);
    }

    // ======================================================================
    // UI HELPERS
    // ======================================================================
    private Panel NewPanel(string panelName, Transform parent, bool stretch)
    {
        RectTransform rt = NewRect(panelName, parent);
        if (stretch) Stretch(rt);

        Panel p = new Panel();
        p.rect = rt;
        p.group = rt.gameObject.AddComponent<CanvasGroup>();
        p.group.alpha = 0f;
        p.target = 0f;
        rt.gameObject.SetActive(false);

        panels.Add(p);
        return p;
    }

    private RectTransform NewRect(string objName, Transform parent)
    {
        GameObject go = new GameObject(objName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private void Place(RectTransform rt, float anchorX, float anchorY, float pivotX, float pivotY, float x, float y, float w, float h)
    {
        rt.anchorMin = new Vector2(anchorX, anchorY);
        rt.anchorMax = new Vector2(anchorX, anchorY);
        rt.pivot = new Vector2(pivotX, pivotY);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    private void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void StretchInset(RectTransform rt, float inset)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    private Image NewImage(string objName, Transform parent, Color color)
    {
        RectTransform rt = NewRect(objName, parent);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private Text NewText(string objName, Transform parent, string content, int size, Color color, TextAnchor align, FontStyle style, bool shadow)
    {
        RectTransform rt = NewRect(objName, parent);
        Text t = rt.gameObject.AddComponent<Text>();
        t.font = font;
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.fontStyle = style;
        t.raycastTarget = false;
        t.supportRichText = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;

        if (shadow)
        {
            Shadow sh = rt.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
            sh.effectDistance = new Vector2(2f, -2f);
        }

        return t;
    }

    private Tracked MakeTracked(Transform parent, string objName, string content, int size, Color color, FontStyle style, float spacing)
    {
        Tracked tr = new Tracked();
        tr.rect = NewRect(objName, parent);

        HorizontalLayoutGroup row = tr.rect.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = spacing;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        ContentSizeFitter fitter = tr.rect.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (c == ' ')
            {
                RectTransform gap = NewRect("Space", tr.rect);
                LayoutElement le = gap.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = size * 0.5f;
                le.preferredHeight = size;
                continue;
            }

            Text letter = NewText("Letter", tr.rect, c.ToString(), size, color, TextAnchor.MiddleCenter, style, false);
            letter.horizontalOverflow = HorizontalWrapMode.Overflow;
            letter.verticalOverflow = VerticalWrapMode.Overflow;
            tr.letters.Add(letter);
        }

        return tr;
    }

    private UIButton MakeButtonBase(Panel owner, Transform parent, string objName)
    {
        UIButton b = new UIButton();
        b.owner = owner;
        b.rect = NewRect(objName, parent);

        b.border = b.rect.gameObject.AddComponent<Image>();
        b.border.color = BorderColor;
        b.border.raycastTarget = false;

        b.fill = NewImage("Fill", b.rect, FillColor);
        StretchInset(b.fill.rectTransform, 2f);

        buttons.Add(b);
        return b;
    }

    private UIButton MakeButton(Panel owner, Transform parent, string objName, string label, int size, float spacing)
    {
        UIButton b = MakeButtonBase(owner, parent, objName);
        b.label = MakeTracked(b.rect, "Label", label, size, TextIdle, FontStyle.Bold, spacing);
        Place(b.label.rect, 0.5f, 0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f);
        return b;
    }

    // ======================================================================
    // GENERATED SPRITES
    // ======================================================================
    private Sprite MakeCircleSprite(bool ring)
    {
        int size = 64;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[size * size];
        float c = (size - 1) / 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - c) / c;
                float dy = (y - c) / c;
                float d = Mathf.Sqrt(dx * dx + dy * dy);

                float a = Mathf.Clamp01((1f - d) * c);
                if (ring) a *= Mathf.Clamp01((d - 0.62f) * c);

                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
    }

    private Sprite MakeVignetteSprite()
    {
        int size = 128;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x / (size - 1f)) * 2f - 1f;
                float ny = (y / (size - 1f)) * 2f - 1f;
                float d = Mathf.Sqrt(nx * nx + ny * ny);

                float s = Mathf.Clamp01((d - 0.45f) / (1.35f - 0.45f));
                s = s * s * (3f - 2f * s);

                pixels[y * size + x] = new Color(0f, 0f, 0f, s * 0.85f);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
    }

    private Sprite MakeGradientSprite()
    {
        int width = 4;
        int height = 128;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;

        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float t = 1f - y / (height - 1f);
            float a = Mathf.Pow(t, 1.6f) * 0.9f;
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = new Color(0f, 0f, 0f, a);
            }
        }

        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f));
    }
}