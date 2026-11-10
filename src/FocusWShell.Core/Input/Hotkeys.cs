using System.Globalization;

namespace FocusWShell.Core.Input;

/// <summary>Modificadores de un atajo, con los valores de <c>RegisterHotKey</c> (MOD_*).</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// Una combinación de teclas para un atajo global: modificadores y una tecla virtual. Se guarda como texto
/// estable ("Ctrl+Alt+Shift+Up") y se muestra en español ("Ctrl+Alt+Mayús+↑").
/// </summary>
public sealed record KeyChord(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private static readonly (string Token, uint Vk, string Display)[] Named =
    [
        ("Up", 0x26, "↑"), ("Down", 0x28, "↓"), ("Left", 0x25, "←"), ("Right", 0x27, "→"),
        ("Back", 0x08, "Retroceso"), ("Space", 0x20, "Espacio"), ("Home", 0x24, "Inicio"), ("End", 0x23, "Fin"),
        ("PageUp", 0x21, "RePág"), ("PageDown", 0x22, "AvPág"), ("Insert", 0x2D, "Insert"), ("Delete", 0x2E, "Supr"),
    ];

    /// <summary>Si la tecla es una que FocusWShell sabe nombrar (letras, dígitos, F1–F24 y las de <see cref="Named"/>).</summary>
    public bool IsKnownKey => KeyToken(VirtualKey) is not null;

    /// <summary>
    /// Un atajo global necesita Ctrl, Alt o Win: con sólo Mayús (o sin nada) se quedaría con una tecla
    /// que el usuario escribe. Las teclas F13–F24 valen solas.
    /// </summary>
    public bool IsValid =>
        IsKnownKey && ((Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0 || VirtualKey is >= 0x7C and <= 0x87);

    /// <summary>Texto estable para guardar: "Ctrl+Alt+Shift+Up".</summary>
    public override string ToString() => Join("Ctrl", "Alt", "Shift", "Win", KeyToken(VirtualKey) ?? $"0x{VirtualKey:X2}");

    /// <summary>Texto para la UI: "Ctrl+Alt+Mayús+↑".</summary>
    public string Display() => Join("Ctrl", "Alt", "Mayús", "Win", KeyDisplay(VirtualKey) ?? $"0x{VirtualKey:X2}");

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = new KeyChord(HotkeyModifiers.None, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var mods = HotkeyModifiers.None;
        uint? vk = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    mods |= HotkeyModifiers.Ctrl;
                    break;
                case "ALT":
                    mods |= HotkeyModifiers.Alt;
                    break;
                case "SHIFT" or "MAYÚS" or "MAYUS":
                    mods |= HotkeyModifiers.Shift;
                    break;
                case "WIN":
                    mods |= HotkeyModifiers.Win;
                    break;
                default:
                    if (vk is not null || ParseKey(raw) is not { } key)
                    {
                        return false;
                    }

                    vk = key;
                    break;
            }
        }

        if (vk is null)
        {
            return false;
        }

        chord = new KeyChord(mods, vk.Value);
        return chord.IsKnownKey;
    }

    private string Join(string ctrl, string alt, string shift, string win, string key)
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl))
        {
            parts.Add(ctrl);
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add(alt);
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add(shift);
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add(win);
        }

        parts.Add(key);
        return string.Join("+", parts);
    }

    private static string? KeyToken(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        _ => Named.FirstOrDefault(n => n.Vk == vk).Token,
    };

    private static string? KeyDisplay(uint vk) => Named.FirstOrDefault(n => n.Vk == vk).Display ?? KeyToken(vk);

    private static uint? ParseKey(string raw)
    {
        if (raw.Length == 1 && char.IsAsciiLetterOrDigit(raw[0]))
        {
            return char.ToUpperInvariant(raw[0]);
        }

        if (raw.Length > 1 && (raw[0] is 'F' or 'f') && int.TryParse(raw[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= 24)
        {
            return (uint)(0x6F + n);
        }

        foreach (var (token, vk, display) in Named)
        {
            if (string.Equals(raw, token, StringComparison.OrdinalIgnoreCase) || raw == display)
            {
                return vk;
            }
        }

        return null;
    }
}

/// <summary>Una acción con atajo global y su combinación por defecto.</summary>
public sealed record HotkeyAction(string Id, string Name, KeyChord Default);

/// <summary>Resultado de combinar los atajos por defecto con los del usuario.</summary>
/// <param name="Chords">Atajo de cada acción, por identificador.</param>
/// <param name="Problems">Una frase por cada atajo del usuario que no se pudo usar.</param>
public sealed record HotkeyMap(IReadOnlyDictionary<string, KeyChord> Chords, IReadOnlyList<string> Problems);

/// <summary>Atajos globales del gestor (05 — hoja de ruta, M1: atajos configurables).</summary>
public static class Hotkeys
{
    public const string Emergency = "restaurar-todas";

    private static KeyChord Cas(uint vk) => new(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, vk);

    /// <summary>Las acciones, en el orden en que se muestran, con los atajos de siempre (Ctrl+Alt+Mayús+…).</summary>
    public static IReadOnlyList<HotkeyAction> Actions { get; } =
    [
        new(Emergency, "Restaurar todas", Cas(0x08)),
        new("al-foco", "Al foco (la ventana activa)", Cas(0x26)),
        new("anterior", "Volver a la anterior", Cas(0x25)),
        new("foco-al-panel", "Foco al panel", Cas(0x28)),
        new("pantalla-completa", "Pantalla completa", Cas(0x46)),
        new("vista", "Cambiar de vista", Cas(0x56)),
        new("pantalla-encendida", "Pantalla encendida", Cas(0x50)),
        .. Enumerable.Range(1, 9).Select(n => new HotkeyAction($"escena-{n}", $"Escena {n}", Cas((uint)(0x30 + n)))),
    ];

    /// <summary>
    /// Atajo de cada acción: el del usuario si es válido y no lo usa otra acción; si no, el de por defecto.
    /// "Restaurar todas" siempre queda con alguno.
    /// </summary>
    public static HotkeyMap Resolve(IReadOnlyDictionary<string, string>? overrides)
    {
        var chords = new Dictionary<string, KeyChord>();
        var problems = new List<string>();
        foreach (var action in Actions)
        {
            var chord = action.Default;
            if (overrides is not null && overrides.TryGetValue(action.Id, out var text))
            {
                if (KeyChord.TryParse(text, out var custom) && custom.IsValid)
                {
                    chord = custom;
                }
                else
                {
                    problems.Add($"«{text}» no vale para {action.Name} (hace falta Ctrl, Alt o Win y una tecla conocida); se usa {action.Default.Display()}.");
                }
            }

            chords[action.Id] = chord;
        }

        // Dos acciones con el mismo atajo: se queda la primera de la lista; la otra vuelve a su atajo de
        // siempre si está libre, o se queda sin atajo.
        var used = new Dictionary<KeyChord, string>();
        foreach (var action in Actions)
        {
            var chord = chords[action.Id];
            if (used.TryGetValue(chord, out var owner))
            {
                var fallback = action.Default;
                bool free = !used.ContainsKey(fallback) && !chords.Any(c => c.Key != action.Id && c.Value == fallback);
                problems.Add(
                    $"{chord.Display()} ya es de {Actions.First(a => a.Id == owner).Name}; " +
                    (free ? $"{action.Name} usa {fallback.Display()}." : $"{action.Name} se queda sin atajo."));
                if (free)
                {
                    chords[action.Id] = fallback;
                    used[fallback] = action.Id;
                }
                else
                {
                    chords.Remove(action.Id);
                }

                continue;
            }

            used[chord] = action.Id;
        }

        return new HotkeyMap(chords, problems);
    }
}
