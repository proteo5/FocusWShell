using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FocusWShell.App.DeckWeb;

/// <summary>Un dispositivo emparejado con el deck por la red (ADR 0021).</summary>
/// <param name="TokenHash">SHA-256 de su token, en hexadecimal: el token en sí sólo lo tiene el dispositivo.</param>
public sealed record DeckWebDevice(string Id, string Name, string TokenHash, DateTimeOffset Paired, DateTimeOffset? LastSeen);

/// <summary>
/// Los dispositivos emparejados, en <c>%LOCALAPPDATA%\FocusWShell\deck-web.json</c>, y el código de un solo uso
/// para emparejar uno nuevo. Seguro entre hilos.
/// </summary>
public sealed class DeckWebDevices
{
    /// <summary>Cuánto vale un código para emparejar.</summary>
    public static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Lock _gate = new();
    private List<DeckWebDevice> _devices;
    private (string Code, DateTime Until)? _code;

    public DeckWebDevices(string path)
    {
        _path = path;
        _devices = Load(path);
    }

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusWShell", "deck-web.json");

    /// <summary>Cambió la lista (para la ventana principal).</summary>
    public event Action? Changed;

    public IReadOnlyList<DeckWebDevice> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _devices];
            }
        }
    }

    /// <summary>Un código nuevo de 6 cifras, válido <see cref="CodeLife"/>. Invalida el anterior.</summary>
    public string NewCode()
    {
        lock (_gate)
        {
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            _code = (code, DateTime.UtcNow + CodeLife);
            _failures = 0;
            return code;
        }
    }

    /// <summary>Intentos fallidos con el código de ahora: al llegar a <see cref="MaxFailures"/>, el código deja de valer.</summary>
    private int _failures;

    /// <summary>Con 6 cifras, 5 intentos dan 1 entre 200 000 de acertar a ciegas antes de que el código se anule.</summary>
    public const int MaxFailures = 5;

    /// <summary>Si el código se anuló por intentos fallidos (para decirlo en la ventana principal).</summary>
    public event Action? CodeBurned;

    /// <summary>Si el código vale (sin gastarlo).</summary>
    public bool IsValidCode(string? code)
    {
        lock (_gate)
        {
            return code is not null && _code is { } c && c.Until > DateTime.UtcNow
                && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(code), Encoding.ASCII.GetBytes(c.Code));
        }
    }

    /// <summary>Empareja un dispositivo con el código y devuelve su token, o <c>null</c> si el código no vale.</summary>
    public string? Pair(string? code, string name)
    {
        string token;
        bool burned = false;
        lock (_gate)
        {
            if (!IsValidCode(code))
            {
                // Un intento fallido con un código vigente cuenta; a los cinco, se anula y hay que pedir otro.
                if (_code is { } current && current.Until > DateTime.UtcNow && ++_failures >= MaxFailures)
                {
                    _code = null;
                    burned = true;
                }
            }
        }

        if (burned)
        {
            CodeBurned?.Invoke();
        }

        lock (_gate)
        {
            if (!IsValidCode(code))
            {
                return null;
            }

            _code = null; // un solo uso
            token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var clean = string.IsNullOrWhiteSpace(name) ? "Tablet" : name.Trim();
            _devices.Add(new DeckWebDevice(Guid.NewGuid().ToString("N")[..8], clean.Length > 40 ? clean[..40] : clean, Hash(token), DateTimeOffset.Now, null));
            Save();
        }

        Changed?.Invoke();
        return token;
    }

    /// <summary>El dispositivo de un token, o <c>null</c>. Anota la última vez que se vio (sin escribir a cada mensaje).</summary>
    public DeckWebDevice? Find(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var hash = Hash(token);
        bool changed = false;
        DeckWebDevice? found;
        lock (_gate)
        {
            int i = _devices.FindIndex(d => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(d.TokenHash), Encoding.ASCII.GetBytes(hash)));
            if (i < 0)
            {
                return null;
            }

            found = _devices[i];
            if (found.LastSeen is not { } seen || DateTimeOffset.Now - seen > TimeSpan.FromMinutes(1))
            {
                found = found with { LastSeen = DateTimeOffset.Now };
                _devices[i] = found;
                Save();
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }

        return found;
    }

    /// <summary>Quita un dispositivo: su token deja de valer al momento.</summary>
    public void Revoke(string id)
    {
        lock (_gate)
        {
            _devices.RemoveAll(d => d.Id == id);
            Save();
        }

        Changed?.Invoke();
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_devices, Options));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static List<DeckWebDevice> Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<List<DeckWebDevice>>(File.ReadAllText(path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
