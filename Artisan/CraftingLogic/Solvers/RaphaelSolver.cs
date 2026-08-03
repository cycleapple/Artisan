using Artisan.Autocraft;
using Artisan.GameInterop;
using Artisan.RawInformation;
using Artisan.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Artisan.CraftingLogic.Solvers
{
    public class RaphaelSolverDefintion : ISolverDefinition
    {
        public Solver Create(CraftState craft, int flavour)
        {
            var key = RaphaelCache.GetKey(craft);
            if (RaphaelCache.HasSolution(craft, out var output))
            {
                return new MacroSolver(output!, craft);
            }
            return craft.CraftExpert ? new ExpertSolver() : new StandardSolver(false);
        }

        public IEnumerable<ISolverDefinition.Desc> Flavours(CraftState craft)
        {
            //if (RaphaelCache.HasSolution(craft, out var solution))
            yield return new(this, 3, 0, $"Raphael Recipe Solver");
        }
    }

    internal static class RaphaelCache
    {
        internal static readonly ConcurrentDictionary<string, Tuple<CancellationTokenSource, Task>> Tasks = [];
        [NonSerialized]
        public static Dictionary<string, RaphaelSolutionConfig> TempConfigs = new();

        public static void Build(CraftState craft, RaphaelSolutionConfig config)
        {
            var key = GetKey(craft);

            if (CLIExists() && !Tasks.ContainsKey(key))
            {
                P.Config.RaphaelSolverCacheV3.TryRemove(key, out _);

                Svc.Log.Information("Spawning Raphael process");

                var manipulation = craft.UnlockedManipulation ? "--manipulation" : "";
                var itemText = $"--recipe-id {craft.RecipeId}";
                var extraArgsBuilder = new StringBuilder();

                extraArgsBuilder.Append($"--initial {craft.InitialQuality} "); // must always have a space after

                if (config.EnsureReliability)
                {
                    Svc.Log.Error("Ensuring reliability is enabled, this may take a while. NO SUPPORT GIVEN IF ENABLED.");
                    extraArgsBuilder.Append($"--adversarial "); // must always have a space after
                }

                if (config.BackloadProgress)
                {
                    extraArgsBuilder.Append($"--backload-progress "); // must always have a space after
                }

                if (config.HeartAndSoul)
                {
                    extraArgsBuilder.Append($"--heart-and-soul "); // must always have a space after
                }

                if (config.QuickInno)
                {
                    extraArgsBuilder.Append($"--quick-innovation "); // must always have a space after
                }

                if (P.Config.RaphaelSolverConfig.MaximumThreads > 0)
                {
                    extraArgsBuilder.Append($"--threads {P.Config.RaphaelSolverConfig.MaximumThreads} "); // must always have a space after
                }

                var process = new Process()
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Join(Path.GetDirectoryName(Svc.PluginInterface.AssemblyLocation.FullName), "raphael-cli.bin"),
                        Arguments = $"solve {itemText} {manipulation} --level {craft.StatLevel} --stats {craft.StatCraftsmanship} {craft.StatControl} {craft.StatCP} {extraArgsBuilder} --output-variables action_ids", // Command to execute
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                Svc.Log.Information(process.StartInfo.Arguments);

                var cts = new CancellationTokenSource();
                cts.Token.Register(() =>
                {
                    try
                    {
                        process?.Kill();
                    }
                    catch (Exception ex)
                    {
                        ex.Log("Couldn't remove process, likely already completed.");
                    }
                    Tasks.TryRemove(key, out var _);
                }
                );
                cts.CancelAfter(TimeSpan.FromMinutes(P.Config.RaphaelSolverConfig.TimeOutMins));

                var task = Task.Run(() =>
                {
                    process.Start();
                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd().Trim();
                    if (process.ExitCode != 0)
                    {
                        DuoLog.Error(error.Split('\r', '\n')[1]);
                        cts.Cancel();
                        return;
                    }
                    cts.Token.ThrowIfCancellationRequested();

                    Svc.Log.Information("Raphael process completed, output generated");
                    var rng = new Random();
                    var ID = rng.Next(50001, 10000000);
                    while (P.Config.RaphaelSolverCacheV3.Any(kv => kv.Value.ID == ID))
                        ID = rng.Next(50001, 10000000);

                    var cleansedOutput = output.Replace("[", "").Replace("]", "").Replace("\"", "").Split(", ").Select(x => int.TryParse(x, out int n) ? n : 0);
                    P.Config.RaphaelSolverCacheV3[key] = new MacroSolverSettings.Macro()
                    {
                        ID = ID,
                        Name = key,
                        Steps = MacroUI.ParseMacro(cleansedOutput),
                        Options = new()
                        {
                            SkipQualityIfMet = false,
                            UpgradeProgressActions = false,
                            UpgradeQualityActions = false,
                            MinCP = craft.StatCP,
                            MinControl = craft.StatControl,
                            MinCraftsmanship = craft.StatCraftsmanship,
                        }
                    };

                    Svc.Log.Information("Raphael macro generated and stored in cache.");
                    if (P.Config.RaphaelSolverCacheV3[key] == null || P.Config.RaphaelSolverCacheV3[key].Steps.Count == 0)
                    {
                        Svc.Log.Error($"Raphael failed to generate a valid macro. This could be one of the following reasons:" +
                            $"\n- If you are not running Windows, Raphael may not be compatible with your OS." +
                            $"\n- You cancelled the generation." +
                            $"\n- Raphael just gave up after not finding a result.{(P.Config.RaphaelSolverConfig.AutoGenerate ? "\nAutomatic generation will be disabled as a result." : "")}");
                        P.Config.RaphaelSolverConfig.AutoGenerate = false;
                        cts.Cancel();
                        return;
                    }

                    static bool autoSwitchOk(uint recipeId)
                    {
                        if (P.Config.RaphaelSolverConfig.AutoSwitchOverManual)
                            return true;

                        if (P.Config.RecipeConfigs.TryGetValue(recipeId, out var cfg))
                            // flavours: 0 = standard, expert; 3 = raphael; otherwise = macro/script
                            return cfg.SolverFlavour is 0 or 3;

                        return true;
                    }

                    if (P.Config.RaphaelSolverConfig.AutoSwitch)
                    {
                        Svc.Log.Information("Auto-switch is enabled, switching solver for recipe if applicable.");
                        if (!P.Config.RaphaelSolverConfig.AutoSwitchOnAll)
                        {
                            Svc.Log.Debug("Switching to Raphael solver - Single");
                            var nopt = CraftingProcessor.GetAvailableSolversForRecipe(craft, true).FirstOrNull(x => x.Name == $"Raphael Recipe Solver");
                            if (nopt is { } opt)
                            {
                                if (autoSwitchOk(craft.Recipe.RowId))
                                {
                                    Svc.Log.Information("AutoSwitchOk, setting");
                                    var config = P.Config.RecipeConfigs.GetValueOrDefault(craft.Recipe.RowId) ?? new();
                                    config.SolverType = opt.Def.GetType().FullName!;
                                    config.SolverFlavour = opt.Flavour;
                                    P.Config.RecipeConfigs[craft.Recipe.RowId] = config;
                                }
                                else
                                    Svc.Log.Information("Never mind, recipe already has a macro assigned");
                            }
                        }
                        else
                        {
                            // Always include the recipe that produced the solution,
                            // even if a future sheet variant does not match the
                            // compatibility scan.
                            var crafts = AllValidCrafts(key, craft.Recipe.CraftType.RowId)
                                .Append(craft)
                                .DistinctBy(x => x.Recipe.RowId)
                                .ToList();
                            Svc.Log.Information($"Applying solver to {crafts.Count} recipes.");
                            var nopt = CraftingProcessor.GetAvailableSolversForRecipe(craft, true).FirstOrNull(x => x.Name == $"Raphael Recipe Solver");
                            if (nopt is { } opt)
                            {
                                var config = P.Config.RecipeConfigs.GetValueOrDefault(craft.Recipe.RowId) ?? new();
                                config.SolverType = opt.Def.GetType().FullName!;
                                config.SolverFlavour = opt.Flavour;
                                foreach (var c in crafts)
                                {
                                    if (autoSwitchOk(c.Recipe.RowId))
                                    {
                                        Svc.Log.Information($"Switching {c.Recipe.RowId} ({c.Recipe.ItemResult.Value.Name}) to Raphael solver");
                                        P.Config.RecipeConfigs[c.Recipe.RowId] = config;
                                    }
                                    else
                                        Svc.Log.Information($"Skipping {c.Recipe.RowId} ({c.Recipe.ItemResult.Value.Name}) because it already has a macro assigned");
                                }
                            }
                        }
                    }
                    Svc.Log.Information("Saving config changes after Raphael generation.");
                    P.Config.Save();

                    Svc.Log.Information("Tidying up task.");
                    Tasks.Remove(key, out var _);
                }, cts.Token);

                Tasks.TryAdd(key, new(cts, task));
            }
        }

        public static string GetKey(CraftState craft)
        {
            return $"{craft.CraftLevel}/{craft.CraftProgress}/{craft.CraftQualityMax}/{craft.CraftDurability}-{craft.StatCraftsmanship}/{craft.StatControl}/{craft.StatCP}-{(craft.CraftExpert ? "Expert" : "Standard")}/{craft.InitialQuality}";
        }

        public static IEnumerable<CraftState> AllValidCrafts(string key, uint craftType)
        {
            var stats = KeyParts(key);
            var recipes = LuminaSheets.RecipeSheet.Values.Where(x =>
                x.CraftType.RowId == craftType &&
                (x.Number == 0 || x.RecipeLevelTable.Value.ClassJobLevel == stats.Level));
            foreach (var recipe in recipes)
            {
                // Cosmic recipes (Number == 0) use the active job level to select
                // their effective RecipeLevelTable. Supplying default stats here
                // made auto-switch filter every cosmic recipe out, including the
                // recipe that just generated this Raphael solution.
                var characterStats = new CharacterStats { Level = stats.Level };
                var state = Crafting.BuildCraftStateForRecipe(characterStats, (Job)((uint)Job.CRP + recipe.CraftType.RowId), recipe);
                if (stats.Prog == state.CraftProgress &&
                    stats.Qual == state.CraftQualityMax &&
                    stats.Dur == state.CraftDurability)
                    yield return state;
            }
        }

        public static (int Level, int Prog, int Qual, int Dur, int Initial, int Crafts, int Control, int CP) KeyParts(string key)
        {
            var parts = key.Split('/');

            int.TryParse(parts[0], out var lvl);
            int.TryParse(parts[1], out var prog);
            int.TryParse(parts[2], out var qual);
            int.TryParse(parts[3].Split('-')[0], out var dur);
            int.TryParse(parts[3].Split('-')[1], out var crafts);
            int.TryParse(parts[4], out var ctrl);
            int.TryParse(parts[5].Split('-')[0], out var cp);
            int.TryParse(parts[6], out var initial);

            return (lvl, prog, qual, dur, initial, crafts, ctrl, cp);
        }

        public static bool HasSolution(CraftState craft, out MacroSolverSettings.Macro? raphaelSolutionConfig)
        {
            foreach (var solution in P.Config.RaphaelSolverCacheV3.OrderByDescending(x => KeyParts(x.Key).Control))
            {
                if (solution.Value.Steps.Count == 0) continue;

                var solKey = KeyParts(solution.Key);

                if (solKey.Level == craft.CraftLevel &&
                    solKey.Prog == craft.CraftProgress &&
                    solKey.Qual == craft.CraftQualityMax &&
                    solKey.Crafts == craft.StatCraftsmanship &&
                    solKey.Control <= craft.StatControl &&
                    solKey.Initial == craft.InitialQuality &&
                    solKey.CP <= craft.StatCP)
                {
                    raphaelSolutionConfig = solution.Value;
                    return true;
                }
            }
            raphaelSolutionConfig = null;
            return false;
        }

        public static bool InProgress(CraftState craft) => Tasks.TryGetValue(GetKey(craft), out var _);

        public static bool InProgressAny() => Tasks.Any();

        internal static bool CLIExists()
        {
            return File.Exists(Path.Join(Path.GetDirectoryName(Svc.PluginInterface.AssemblyLocation.FullName), "raphael-cli.bin"));
        }

        public static bool DrawRaphaelDropdown(CraftState craft, bool liveStats = true)
        {
            bool changed = false;
            var config = P.Config.RecipeConfigs.GetValueOrDefault(craft.RecipeId) ?? new();
            if (CLIExists())
            {
                var hasSolution = HasSolution(craft, out var solution);
                var key = GetKey(craft);

                if (!TempConfigs.ContainsKey(key))
                {
                    TempConfigs.Add(key, new());
                    TempConfigs[key].EnsureReliability = P.Config.RaphaelSolverConfig.AllowEnsureReliability;
                    TempConfigs[key].BackloadProgress = P.Config.RaphaelSolverConfig.AllowBackloadProgress;
                    TempConfigs[key].HeartAndSoul = P.Config.RaphaelSolverConfig.ShowSpecialistSettings && craft.Specialist;
                    TempConfigs[key].QuickInno = P.Config.RaphaelSolverConfig.ShowSpecialistSettings && craft.Specialist;
                }

                var opt = CraftingProcessor.GetAvailableSolversForRecipe(craft, true).FirstOrNull(x => x.Name == $"Raphael Recipe Solver");
                var solverIsRaph = config.SolverType == opt?.Def.GetType().FullName!;
                if (hasSolution)
                {
                    var curStats = CharacterStats.GetCurrentStats();

                    if (!solverIsRaph)
                    {
                        if (liveStats)
                        {
                            ImGuiEx.TextCentered("Raphael 解法已產生。（點擊切換）");
                            if (ImGui.IsItemClicked())
                            {
                                config.SolverType = opt?.Def.GetType().FullName!;
                                config.SolverFlavour = (int)(opt?.Flavour);
                                changed = true;
                            }
                        }
                        else
                        {
                            ImGuiEx.TextCentered("Raphael 解法已產生。");
                        }
                    }
                    else
                    {
                        ImGuiEx.TextCentered($"解法索引鍵：{key}");
                        var playerIsJob = Player.JobId == craft.Recipe.CraftType.RowId + 8;
                        var parts = KeyParts(key);
                        if (!playerIsJob)
                            ImGuiEx.TextCentered(ImGuiColors.DalamudOrange, "目前不是對應製作職業。");
                        else
                        {
                            if (curStats.Craftsmanship == craft.StatCraftsmanship)
                                ImGuiEx.TextCentered(ImGuiColors.HealerGreen, $"作業精度符合解法需求：{parts.Crafts}。");
                            else
                            {
                                ImGuiEx.TextCentered(ImGuiColors.DPSRed, $"解法所需作業精度（{craft.StatCraftsmanship}）與目前數值（{curStats.Craftsmanship}）不同。\n解決此差異前不會使用 Raphael。");
                                var foodIsCrafts = ConsumableChecker.GetItemConsumableProperties(LuminaSheets.ItemSheet[config.RequiredFood], false)?.Params.Any(x => x.BaseParam.RowId is 70);
                                if (foodIsCrafts == true)
                                    ImGuiEx.TextCentered(ImGuiColors.DalamudOrange, "（設定的食物會提高作業精度，套用增益後可能即可解決此問題）");

                                var potIsCrafts = ConsumableChecker.GetItemConsumableProperties(LuminaSheets.ItemSheet[config.RequiredPotion], false)?.Params.Any(x => x.BaseParam.RowId is 70);
                                if (potIsCrafts == true)
                                    ImGuiEx.TextCentered(ImGuiColors.DalamudOrange, "（設定的藥品會提高作業精度，套用增益後可能即可解決此問題）");

                                if ((foodIsCrafts == null || foodIsCrafts == false) && (potIsCrafts == null || potIsCrafts == false))
                                    ImGuiEx.TextCentered(ImGuiColors.DalamudOrange, "（目前有未設為指定食物／藥品、但會提高作業精度的進食／藥物增益；移除增益後可能即可解決）");

                                var diffPos = Math.Abs(craft.StatCraftsmanship - curStats.Craftsmanship);
                                var diffAct = (craft.StatCraftsmanship - curStats.Craftsmanship);
                                if (diffPos % 5 == 0)
                                    if (diffAct > 0)
                                        ImGuiEx.TextCentered(ImGuiColors.DalamudOrange, "（產生此解法時可能有部隊作業精度增益，但目前已失效）");
                                    else
                                        ImGuiEx.TextCentered(ImGuiColors.DalamudOrange, "（目前可能有產生此解法時尚未生效的部隊作業精度增益）");
                            }
                        }

                    }
                }
                else
                {
                    ImGuiEx.TextCentered(ImGuiColors.DalamudRed, "尚未產生 Raphael 解法。");
                    if (P.Config.RaphaelSolverConfig.AutoGenerate && CraftingProcessor.GetAvailableSolversForRecipe(craft, true).Any() && (!craft.CraftExpert || (craft.CraftExpert && P.Config.RaphaelSolverConfig.GenerateOnExperts)))
                    {
                        if (liveStats && Player.JobId == craft.Recipe.CraftType.RowId + 8)
                        {
                            Build(craft, TempConfigs[key]);
                        }
                        else
                        {
                            ImGuiEx.TextCentered(ImGuiColors.DalamudOrange, $"切換為 {Svc.Data.GetExcelSheet<ClassJob>().GetRow(craft.Recipe.CraftType.RowId + 8).Abbreviation} 後將自動產生 Raphael 解法。");
                        }
                    }
                }

                ImGui.Separator();

                var inProgress = InProgress(craft);
                var raphChanges = false;

                if (inProgress)
                    ImGui.BeginDisabled();

                if (P.Config.RaphaelSolverConfig.AllowEnsureReliability)
                    raphChanges |= ImGui.Checkbox($"確保成功率##{key}Reliability", ref TempConfigs[key].EnsureReliability);
                if (P.Config.RaphaelSolverConfig.AllowBackloadProgress)
                    raphChanges |= ImGui.Checkbox($"將進展安排在後段##{key}Progress", ref TempConfigs[key].BackloadProgress);
                if (P.Config.RaphaelSolverConfig.ShowSpecialistSettings && craft.Specialist)
                    raphChanges |= ImGui.Checkbox($"允許使用專心致志##{key}HS", ref TempConfigs[key].HeartAndSoul);
                if (P.Config.RaphaelSolverConfig.ShowSpecialistSettings && craft.Specialist)
                    raphChanges |= ImGui.Checkbox($"允許使用快速改革##{key}QI", ref TempConfigs[key].QuickInno);

                changed |= raphChanges;

                if (inProgress)
                    ImGui.EndDisabled();

                if (!inProgress)
                {
                    if (ImGui.Button("產生 Raphael 解法", new Vector2(ImGui.GetContentRegionAvail().X, 25f.Scale())))
                    {
                        Build(craft, TempConfigs[key]);
                    }
                }
                else
                {
                    if (ImGui.Button("取消產生 Raphael 解法", new Vector2(ImGui.GetContentRegionAvail().X, 25f.Scale())))
                    {
                        Tasks.TryRemove(key, out var task);
                        task.Item1.Cancel();
                    }
                }

                if (TempConfigs[key].EnsureReliability && ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text("已啟用確保品質。此選項可能造成問題，啟用時不提供相關支援。");
                    ImGui.EndTooltip();
                }

                if (TempConfigs[key].HeartAndSoul || TempConfigs[key].QuickInno)
                {
                    ImGui.Text("已啟用專家技能，可能大幅降低求解速度。");
                }

                if (inProgress)
                {
                    ImGuiEx.TextCentered("正在產生……");
                }
            }

            return changed;
        }
    }

    public class RaphaelSolverSettings
    {
        public bool AllowEnsureReliability = false;
        public bool AllowBackloadProgress = true;
        public bool ShowSpecialistSettings = false;
        public bool ExactCraftsmanship = false;
        public bool AutoGenerate = false;
        public bool AutoSwitch = false;
        public bool AutoSwitchOnAll = false;
        public bool AutoSwitchOverManual = true;
        public int MaximumThreads = 0;
        public bool GenerateOnExperts = false;
        public int TimeOutMins = 1;

        public bool Draw()
        {
            bool changed = false;

            ImGui.Indent();
            ImGui.TextWrapped("Raphael 設定會影響效能與記憶體用量。若可用記憶體不多，建議不要變更設定；建議至少保留 2 GB 可用記憶體。");

            if (ImGui.SliderInt("最大執行緒數", ref MaximumThreads, 0, Environment.ProcessorCount))
            {
                P.Config.Save();
            }
            ImGuiEx.TextWrapped("預設會使用所有可用資源；效能較低的電腦可減少 CPU 使用量，但求解速度也會降低。（0＝全部）");

            changed |= ImGui.Checkbox("產生巨集時確保 100% 成功率", ref AllowEnsureReliability);
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(new System.Numerics.Vector4(255, 0, 0, 1), "確保成功率未必始終有效，且會大量使用 CPU 與記憶體；建議至少保留 16 GB 可用記憶體。啟用此功能時不提供相關支援。");
            ImGui.PopTextWrapPos();
            changed |= ImGui.Checkbox("產生巨集時允許將進展安排在後段", ref AllowBackloadProgress);
            changed |= ImGui.Checkbox("可用時顯示專家選項", ref ShowSpecialistSettings);
            changed |= ImGui.Checkbox("沒有有效解法時自動產生", ref AutoGenerate);

            if (AutoGenerate)
            {
                ImGui.Indent();
                changed |= ImGui.Checkbox("為高難度配方產生解法", ref GenerateOnExperts);
                ImGui.Unindent();
            }

            changed |= ImGui.Checkbox("解法產生後自動切換至 Raphael 求解器", ref AutoSwitch);

            if (AutoSwitch)
            {
                ImGui.Indent();
                changed |= ImGui.Checkbox("套用至所有有效製作", ref AutoSwitchOnAll);
                changed |= ImGui.Checkbox("也套用至已指派巨集的製作", ref AutoSwitchOverManual);
                ImGui.Unindent();
            }

            changed |= ImGui.SliderInt("產生解法逾時（分鐘）", ref TimeOutMins, 1, 15);

            ImGuiComponents.HelpMarker("若產生解法超過此分鐘數，將取消求解工作。");

            if (ImGui.Button($"清除 Raphael 巨集快取（目前儲存 {P.Config.RaphaelSolverCacheV3.Count} 筆）"))
            {
                P.Config.RaphaelSolverCacheV3.Clear();
                changed |= true;
            }

            ImGui.Unindent();
            return changed;
        }
    }

    public class RaphaelSolutionConfig
    {
        public bool EnsureReliability = false;
        public bool BackloadProgress = false;
        public bool HeartAndSoul = false;
        public bool QuickInno = false;
        public string Macro = string.Empty;

        public int MinCP = 0;
        public int MinControl = 0;
        public int ExactCraftsmanship = 0;

        [NonSerialized]
        public bool HasChanges = false;
    }
}
