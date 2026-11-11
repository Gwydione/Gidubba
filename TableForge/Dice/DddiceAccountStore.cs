using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TableForge.Dice;

/// <summary>
/// A connected dddice account as TableForge holds it in memory. <see cref="Token"/> is plain text only here; on disk it exists
/// only DPAPI-protected. <see cref="RoomSlug"/>, <see cref="ThemeId"/>, <see cref="ThemeName"/> and <see cref="AccountId"/> are not secret.
/// <see cref="AccountId"/> is the dddice user uuid the token belongs to (null until known, e.g. a file saved by 1.5.0 or earlier);
/// once it is known, <see cref="RoomSlug"/> is a room remembered for that account.
/// </summary>
public sealed record DddiceAccount(string Token, string? DisplayName, string? RoomSlug, string? ThemeId, string? ThemeName, string? AccountId = null)
{
    /// <summary>Never includes the token, so an account can't leak it through logging or a debugger display.</summary>
    public override string ToString() => $"dddice account ({DisplayName ?? "Connected"}, theme {ThemeId ?? "none"})";
}

public enum DddiceAccountFileState { None, Loaded, Unreadable }

/// <summary>What was found on disk: nothing, a usable account, or a file that could not be read or decrypted.</summary>
public sealed record DddiceAccountLoad(DddiceAccountFileState State, DddiceAccount? Account = null);

/// <summary>Where the connected account lives between runs. Guest mode never touches it.</summary>
public interface IDddiceAccountStore
{
    DddiceAccountLoad Load();
    void Save(DddiceAccount account);
    void Delete();
}

/// <summary>
/// <c>&lt;data folder&gt;\dddice-account.json</c>, next to dice-provider.txt (so TABLEFORGE_DATA_DIR isolates it like everything
/// else). The token is protected with Windows DPAPI for the current Windows user plus a fixed TableForge entropy, and stored as
/// base64; nothing in the file is the token in plain text. Writes go to a temp file that then replaces the old one.
/// </summary>
public sealed class DddiceAccountStore(string dataFolder) : IDddiceAccountStore
{
    public const string FileName = "dddice-account.json";
    private const int Format = 1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TableForge dddice account token v1");

    public string FilePath { get; } = Path.Combine(dataFolder, FileName);

    public DddiceAccountLoad Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new(DddiceAccountFileState.None);
            var json = JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
            if (json is null || (int?)json["format"] != Format || Text(json["protectedToken"]) is not { Length: > 0 } protectedToken)
                return new(DddiceAccountFileState.Unreadable);

            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), Entropy, DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(bytes);
            if (token.Length == 0) return new(DddiceAccountFileState.Unreadable);

            return new(DddiceAccountFileState.Loaded, new DddiceAccount(token,
                Text(json["displayName"]), Text(json["roomSlug"]), Text(json["themeId"]), Text(json["themeName"]), Text(json["accountId"])));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException
                                       or CryptographicException or InvalidOperationException or ArgumentException)
        {
            return new(DddiceAccountFileState.Unreadable); // corrupt, another Windows user's, or unreadable: reconnect, never crash
        }
    }

    public void Save(DddiceAccount account)
    {
        var protectedToken = ProtectedData.Protect(Encoding.UTF8.GetBytes(account.Token), Entropy, DataProtectionScope.CurrentUser);
        var json = new JsonObject
        {
            ["format"] = Format,
            ["protectedToken"] = Convert.ToBase64String(protectedToken),
            ["displayName"] = account.DisplayName,
            ["roomSlug"] = account.RoomSlug,
            ["themeId"] = account.ThemeId,
            ["themeName"] = account.ThemeName,
            ["accountId"] = account.AccountId,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temp, FilePath, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(FilePath)) File.Delete(FilePath);
        if (File.Exists(FilePath + ".tmp")) File.Delete(FilePath + ".tmp");
    }

    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>
/// The room Gidubba last used for each dddice account, keyed by the account's dddice user uuid. It holds no token or other credential,
/// so it outlives Disconnect: connecting the same account again (now or after a restart) reuses its room instead of creating another.
/// A room is only ever looked up for the account it was remembered for.
/// </summary>
public interface IDddiceRoomMemory
{
    string? Find(string accountId);
    void Remember(string accountId, string roomSlug);
}

/// <summary>For tests and for a connection without a data folder: remembers for this run only.</summary>
public sealed class InMemoryDddiceRoomMemory : IDddiceRoomMemory
{
    private readonly Dictionary<string, string> _rooms = new(StringComparer.Ordinal);
    public string? Find(string accountId) => _rooms.GetValueOrDefault(accountId);
    public void Remember(string accountId, string roomSlug) => _rooms[accountId] = roomSlug;
}

/// <summary>
/// <c>&lt;data folder&gt;\dddice-rooms.json</c>: <c>{"format":1,"rooms":{"&lt;account uuid&gt;":"&lt;room slug&gt;"}}</c>. Plain JSON (nothing in it
/// is secret) and platform-neutral. A missing, corrupt or unwritable file only means a room may have to be created again; it never
/// stops dddice from working.
/// </summary>
public sealed class DddiceRoomMemoryStore(string dataFolder) : IDddiceRoomMemory
{
    public const string FileName = "dddice-rooms.json";
    private const int Format = 1;

    public string FilePath { get; } = Path.Combine(dataFolder, FileName);

    public string? Find(string accountId) =>
        Read().TryGetValue(accountId, out var slug) ? slug : null;

    public void Remember(string accountId, string roomSlug)
    {
        try
        {
            var rooms = Read();
            if (rooms.TryGetValue(accountId, out var known) && known == roomSlug) return;
            rooms[accountId] = roomSlug;
            var map = new JsonObject();
            foreach (var (id, slug) in rooms) map[id] = slug;
            var json = new JsonObject { ["format"] = Format, ["rooms"] = map };
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Read-only, locked or unwritable: the room still works for this run. Leave no half-written temp file behind.
            try { if (File.Exists(FilePath + ".tmp")) File.Delete(FilePath + ".tmp"); } catch (Exception) { /* nothing more to do */ }
        }
    }

    private Dictionary<string, string> Read()
    {
        var rooms = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(FilePath)) return rooms;
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject json || (int?)json["format"] != Format || json["rooms"] is not JsonObject map)
                return rooms;
            foreach (var (id, node) in map)
                if (!string.IsNullOrWhiteSpace(id) && node is JsonValue v && v.TryGetValue<string>(out var slug) && !string.IsNullOrWhiteSpace(slug))
                    rooms[id] = slug;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException
                                       or FormatException or ArgumentException or System.Security.SecurityException)
        {
            rooms.Clear(); // unreadable: as if nothing were remembered
        }
        return rooms;
    }
}
