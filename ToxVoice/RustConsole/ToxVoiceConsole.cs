using System.Text;
using ToxVoice.Persistence;

namespace ToxVoice.RustConsole;

public static class ToxVoiceConsole
{
    private static readonly StringBuilder JsonBuilder = new();
    private static readonly ConsoleSystem.Command ToxVoiceCommand = new()
    {
        Name = "toxvoice",
        Parent = "global",
        FullName = "global.toxvoice",
        ServerAdmin = true,
        Variable = false,
        Call = HandleToxVoiceCommand
    };
    public static void RegisterCommands()
    {
        ConsoleSystem.Index.Server.Dict.TryAdd(ToxVoiceCommand.FullName, ToxVoiceCommand);
        ConsoleSystem.Index.Server.GlobalDict.TryAdd(ToxVoiceCommand.Name, ToxVoiceCommand);

        var allCommands = ConsoleSystem.Index.All.ToList();
        if (allCommands.Any(c => c.Name == ToxVoiceCommand.Name && c.Parent == ToxVoiceCommand.Parent))
            return;

        allCommands.Add(ToxVoiceCommand);
        ConsoleSystem.Index.All = allCommands.ToArray();
    }

    public static void UnregisterCommands()
    {
        if (ConsoleSystem.Index.Server.Dict.ContainsKey(ToxVoiceCommand.FullName))
        {
            ConsoleSystem.Index.Server.Dict.Remove(ToxVoiceCommand.FullName);
        }

        if (ConsoleSystem.Index.Server.GlobalDict.ContainsKey(ToxVoiceCommand.Name))
        {
            ConsoleSystem.Index.Server.GlobalDict.Remove(ToxVoiceCommand.Name);
        }

        var toxVoiceCommandEntry = ConsoleSystem.Index.All.FirstOrDefault(c => c.Name == ToxVoiceCommand.Name && c.Parent == ToxVoiceCommand.Parent);
        if (toxVoiceCommandEntry == null)
            return;

        var allCommands = ConsoleSystem.Index.All.ToList();
        allCommands.Remove(toxVoiceCommandEntry);
        ConsoleSystem.Index.All = allCommands.ToArray();
    }
    private static void HandleToxVoiceCommand(ConsoleSystem.Arg arg)
    {
        try
        {
            var args = arg.FullString.Split(' ');
            if (args.Length < 2)
            {
                arg.ReplyWith("[ToxVoice] Invalid command. Usage: toxvoice <subcommand> <parameter>");
                return;
            }

            var subcommand = args[0].ToLower();
            var parameter = args[1];

            JsonBuilder.AppendLine("{");

            switch (subcommand)
            {
                case "steam":
                    if (ulong.TryParse(parameter, out _))
                    {
                        var toxVoiceUserId = ToxVoicePersistence.GetToxVoiceIdFromSteamId(parameter);
                        if (!string.IsNullOrEmpty(toxVoiceUserId))
                        {
                            JsonBuilder.AppendLine($"  \"SteamID\": {parameter},");
                            JsonBuilder.AppendLine($"  \"ToxVoiceUserID\": \"{toxVoiceUserId}\"");
                        }
                        else
                        {
                            JsonBuilder.AppendLine($"  \"Error\": \"No ToxVoiceUserID found for SteamID: {parameter}\"");
                        }
                    }
                    else
                    {
                        JsonBuilder.AppendLine("  \"Error\": \"Invalid SteamID. Please provide a valid SteamID.\"");
                    }
                    break;

                case "id":
                    if (Guid.TryParse(parameter, out _))
                    {
                        var steamId = ToxVoicePersistence.GetSteamIdFromToxVoiceId(parameter);
                        if (!string.IsNullOrEmpty(steamId))
                        {
                            JsonBuilder.AppendLine($"  \"ToxVoiceUserID\": \"{parameter}\",");
                            JsonBuilder.AppendLine($"  \"SteamID\": {steamId}");
                        }
                        else
                        {
                            JsonBuilder.AppendLine($"  \"Error\": \"No SteamID found for ToxVoiceUserID: {parameter}\"");
                        }
                    }
                    else
                    {
                        JsonBuilder.AppendLine("  \"Error\": \"Invalid ToxVoiceUserID. Please provide a valid GUID.\"");
                    }
                    break;
                case "reset":
                    if (parameter == "all")
                    {
                        ToxVoicePersistence.ResetAllViolations();
                        arg.ReplyWith("[ToxVoice] All player violations have been reset.");
                        return;
                    }
                    else if (ulong.TryParse(parameter, out _))
                    {
                        ToxVoicePersistence.ResetPlayerViolation(parameter);
                        arg.ReplyWith($"[ToxVoice] Violations for player with SteamID {parameter} have been reset.");
                        return;
                    }
                    else
                    {
                        arg.ReplyWith("[ToxVoice] Invalid parameter. Please provide a valid SteamID or use 'all' to reset all violations.");
                        return;
                    }
                    break;
                default:
                    JsonBuilder.AppendLine($"  \"Error\": \"Unknown subcommand: {subcommand}\"");
                    break;
            }

            JsonBuilder.AppendLine("}");

            var jsonString = JsonBuilder.ToString();
            arg.ReplyWith($"[ToxVoice]\n{jsonString}");
        }
        finally
        {
            JsonBuilder.Clear();
        }
    }
}