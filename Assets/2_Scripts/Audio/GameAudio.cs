using FMOD.Studio;
using FMODUnity;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Game-wide audio: starts the background music when the game starts and plays the mouse click
/// sound when the player presses a button or a hospital room.
/// <para>
/// Place one in the first scene and assign the FMOD events. It survives scene loads, and a second
/// copy in a later scene destroys itself so the music never restarts or doubles up.
/// </para>
/// <para>
/// UI screens call <see cref="RegisterButtonClicks"/> with their document root so every
/// <see cref="Button"/> inside it, including ones created later, plays the click sound.
/// Other clickable things call <see cref="PlayClick"/> directly.
/// </para>
/// </summary>
public class GameAudio : MonoBehaviour
{
    [Tooltip("Looping music started when the game starts, e.g. event:/Background Music.")]
    [SerializeField] EventReference backgroundMusicEvent;
    [Tooltip("One-shot played when a button or hospital room is clicked, e.g. event:/Mouse click.")]
    [SerializeField] EventReference mouseClickEvent;

    static GameAudio instance;
    EventInstance backgroundMusic;

    /// <summary>Plays the mouse click sound once. Does nothing if there is no GameAudio in the scene.</summary>
    public static void PlayClick()
    {
        if (instance != null && !instance.mouseClickEvent.IsNull)
        {
            RuntimeManager.PlayOneShot(instance.mouseClickEvent);
        }
    }

    /// <summary>Plays the click sound whenever a button inside <paramref name="root"/> is pressed.</summary>
    public static void RegisterButtonClicks(VisualElement root)
    {
        root?.RegisterCallback<PointerDownEvent>(HandleUiPress, TrickleDown.TrickleDown);
    }

    public static void UnregisterButtonClicks(VisualElement root)
    {
        root?.UnregisterCallback<PointerDownEvent>(HandleUiPress, TrickleDown.TrickleDown);
    }

    // Uses the press rather than ClickEvent: ClickEvent arrives after the button's clicked handler,
    // which may already have disabled or removed the button. The target can be a child of the
    // button (e.g. an icon), so look up the hierarchy.
    static void HandleUiPress(PointerDownEvent evt)
    {
        if (evt.button != (int)MouseButton.LeftMouse)
        {
            return;
        }

        for (VisualElement element = evt.target as VisualElement; element != null; element = element.parent)
        {
            if (element is Button)
            {
                if (element.enabledInHierarchy)
                {
                    PlayClick();
                }

                return;
            }
        }
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (instance != this)
        {
            return;
        }

        if (backgroundMusicEvent.IsNull)
        {
            Debug.LogWarning("GameAudio: no backgroundMusicEvent assigned.", this);
            return;
        }

        backgroundMusic = RuntimeManager.CreateInstance(backgroundMusicEvent);
        backgroundMusic.start();
    }

    void OnDestroy()
    {
        if (instance != this)
        {
            return;
        }

        instance = null;
        if (backgroundMusic.isValid())
        {
            backgroundMusic.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            backgroundMusic.release();
            backgroundMusic.clearHandle();
        }
    }
}
