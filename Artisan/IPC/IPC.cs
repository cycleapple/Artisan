using Artisan.Autocraft;
using Artisan.CraftingLists;
using Artisan.CraftingLogic;
using Artisan.CraftingLogic.Solvers;
using Artisan.GameInterop;
using Artisan.RawInformation;
using Artisan.RawInformation.Character;
using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.DalamudServices;
using ECommons.ExcelServices;
using ECommons.Logging;
using OtterGui;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Artisan.IPC
{
    internal static class IPC
    {
        private const int CosmicRecipeSelectionTimeoutMs = 15_000;
        private static readonly ConcurrentDictionary<ushort, string> CraftRequestFailures = new();

        private static bool stopCraftingRequest;

        public static bool StopCraftingRequest
        {
            get => stopCraftingRequest;
            set
            {
                if (value)
                {
                    StopCrafting();
                }
                else
                {
                    if (!Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.WaitingForDutyFinder] && !Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty])
                        ResumeCrafting();
                }
                stopCraftingRequest = value;
            }
        }

        public static ArtisanMode CurrentMode;
        internal static void Init()
        {
            Svc.PluginInterface.GetIpcProvider<bool>("Artisan.GetEnduranceStatus").RegisterFunc(GetEnduranceStatus);
            Svc.PluginInterface.GetIpcProvider<bool, object>("Artisan.SetEnduranceStatus").RegisterAction(SetEnduranceStatus);

            Svc.PluginInterface.GetIpcProvider<bool>("Artisan.IsListRunning").RegisterFunc(IsListRunning);
            Svc.PluginInterface.GetIpcProvider<bool>("Artisan.IsListPaused").RegisterFunc(IsListPaused);
            Svc.PluginInterface.GetIpcProvider<bool, object>("Artisan.SetListPause").RegisterAction(SetListPause);

            Svc.PluginInterface.GetIpcProvider<bool>("Artisan.GetStopRequest").RegisterFunc(GetStopRequest);
            Svc.PluginInterface.GetIpcProvider<bool, object>("Artisan.SetStopRequest").RegisterAction(SetStopRequest);

            Svc.PluginInterface.GetIpcProvider<ushort, int, object>("Artisan.CraftItem").RegisterAction(CraftX);
            Svc.PluginInterface.GetIpcProvider<bool>("Artisan.IsBusy").RegisterFunc(IsBusy);
            Svc.PluginInterface.GetIpcProvider<ushort, int>("Artisan.GetRaphaelStatus").RegisterFunc(GetRaphaelStatus);
            Svc.PluginInterface.GetIpcProvider<ushort, string>("Artisan.GetRaphaelFailure").RegisterFunc(GetRaphaelFailure);
            Svc.PluginInterface.GetIpcProvider<uint, string, bool>("Artisan.SetTemporarySolver").RegisterFunc(SetTemporarySolver);
            Svc.PluginInterface.GetIpcProvider<uint, uint, bool, bool>("Artisan.SetTemporaryFood").RegisterFunc(SetTemporaryFood);
            Svc.PluginInterface.GetIpcProvider<uint, uint, bool, bool>("Artisan.SetTemporaryPotion").RegisterFunc(SetTemporaryPotion);
            Svc.PluginInterface.GetIpcProvider<uint, object>("Artisan.ClearTemporaryRecipeSettings").RegisterAction(ClearTemporaryRecipeSettings);
            Svc.PluginInterface.GetIpcProvider<object>("Artisan.ClearAllTemporarySettings").RegisterAction(ClearAllTemporarySettings);
            Svc.PluginInterface.GetIpcProvider<uint, string[]>("Artisan.GetAvailableSolvers").RegisterFunc(GetAvailableSolvers);
            Svc.PluginInterface.GetIpcProvider<bool, uint[]>("Artisan.GetAvailableFood").RegisterFunc(GetAvailableFood);
            Svc.PluginInterface.GetIpcProvider<bool, uint[]>("Artisan.GetAvailablePots").RegisterFunc(GetAvailablePots);
        }

        internal static void Dispose()
        {
            Svc.PluginInterface.GetIpcProvider<bool>("Artisan.GetEnduranceStatus").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<bool, object>("Artisan.SetEnduranceStatus").UnregisterAction();

            Svc.PluginInterface.GetIpcProvider<bool>("Artisan.IsListRunning").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<bool>("Artisan.IsListPaused").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<bool, object>("Artisan.SetListPause").UnregisterAction();

            Svc.PluginInterface.GetIpcProvider<bool>("Artisan.GetStopRequest").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<bool, object>("Artisan.SetStopRequest").UnregisterAction();

            Svc.PluginInterface.GetIpcProvider<ushort, int, object>("Artisan.CraftItem").UnregisterAction();
            Svc.PluginInterface.GetIpcProvider<bool>("Artisan.IsBusy").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<ushort, int>("Artisan.GetRaphaelStatus").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<ushort, string>("Artisan.GetRaphaelFailure").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<uint, string, bool>("Artisan.SetTemporarySolver").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<uint, uint, bool, bool>("Artisan.SetTemporaryFood").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<uint, uint, bool, bool>("Artisan.SetTemporaryPotion").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<uint, object>("Artisan.ClearTemporaryRecipeSettings").UnregisterAction();
            Svc.PluginInterface.GetIpcProvider<object>("Artisan.ClearAllTemporarySettings").UnregisterAction();
            Svc.PluginInterface.GetIpcProvider<uint, string[]>("Artisan.GetAvailableSolvers").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<bool, uint[]>("Artisan.GetAvailableFood").UnregisterFunc();
            Svc.PluginInterface.GetIpcProvider<bool, uint[]>("Artisan.GetAvailablePots").UnregisterFunc();
            ClearAllTemporarySettings();
        }

        static bool GetEnduranceStatus()
        {
            return Endurance.Enable;
        }

        static void SetEnduranceStatus(bool s)
        {
            Endurance.ToggleEndurance(s);
        }

        static bool IsListRunning()
        {
            return CraftingListUI.Processing;
        }

        static bool IsListPaused()
        {
            return CraftingListUI.Processing && CraftingListFunctions.Paused;
        }

        static void SetListPause(bool s)
        {
            if (IsListPaused())
                CraftingListFunctions.Paused = s;
        }

        static bool GetStopRequest()
        {
            return StopCraftingRequest;
        }

        static void SetStopRequest(bool s)
        {
            if (s)
                DuoLog.Information("Artisan has been requested to stop by an external plugin.");
            else
                DuoLog.Information("Artisan has been requested to restart by an external plugin.");

            StopCraftingRequest = s;
        }

        public unsafe static void CraftX(ushort recipeId, int amount)
        {
            CraftRequestFailures.TryRemove(recipeId, out _);

            if (LuminaSheets.RecipeSheet!.TryGetFirst(x => x.Value.RowId == recipeId, out var recipe))
            {
                var recipeConfig = P.Config.RecipeConfigs.GetValueOrDefault(recipeId);
                var usesRaphael = recipeConfig?.CurrentSolverType.Contains("Raphael") == true;
                var expectedJobId = recipe.Value.CraftType.RowId + 8;
                if (usesRaphael && (uint)CharacterInfo.JobID == expectedJobId)
                {
                    var stats = CharacterStats.GetCurrentStats();
                    if (recipeConfig is { } config &&
                        ((config.FoodEnabled && !ConsumableChecker.IsFooded(config)) ||
                         (config.PotionEnabled && !ConsumableChecker.IsPotted(config))))
                    {
                        var expectedJob = (Job)expectedJobId;
                        stats = CharacterStats.GetBaseStatsForClassHeuristic(expectedJob);
                        stats.AddConsumables(
                            new(config.RequiredFood, config.RequiredFoodHQ),
                            new(config.RequiredPotion, config.RequiredPotionHQ),
                            CharacterInfo.FCCraftsmanshipbuff);
                    }

                    var craft = Crafting.BuildCraftStateForRecipe(stats, (Job)expectedJobId, recipe.Value);
                    if (craft != null && !RaphaelCache.HasSolution(craft, out _))
                    {
                        if (RaphaelCache.GetGenerationStatus(recipeId) == RaphaelCache.GenerationStatus.Failed)
                        {
                            DuoLog.Error($"無法開始配方 {recipeId}：{RaphaelCache.GetFailure(recipeId)}");
                            return;
                        }

                        RaphaelCache.Build(craft, new RaphaelSolutionConfig
                        {
                            EnsureReliability = P.Config.RaphaelSolverConfig.AllowEnsureReliability,
                            BackloadProgress = P.Config.RaphaelSolverConfig.AllowBackloadProgress,
                            HeartAndSoul = P.Config.RaphaelSolverConfig.ShowSpecialistSettings && craft.Specialist,
                            QuickInno = P.Config.RaphaelSolverConfig.ShowSpecialistSettings && craft.Specialist,
                        });

                        var waitMs = (P.Config.RaphaelSolverConfig.TimeOutMins * 60 * 1000) + 5000;
                        P.TM.Enqueue(() => RaphaelCache.GetGenerationStatus(recipeId) != RaphaelCache.GenerationStatus.InProgress, waitMs, "WaitingForRaphael");
                        P.TM.Enqueue(() =>
                        {
                            if (RaphaelCache.HasSolution(craft, out _))
                                QueueCraftX(recipe.Value, recipeId, amount);
                            else
                                DuoLog.Error($"無法開始配方 {recipeId}：{RaphaelCache.GetFailure(recipeId)}");
                        });
                        return;
                    }
                }

                QueueCraftX(recipe.Value, recipeId, amount);
            }
            else
            {
                throw new Exception("RecipeID not found.");
            }
        }

        private static unsafe void QueueCraftX(Lumina.Excel.Sheets.Recipe recipe, ushort recipeId, int amount)
        {
            var selectionDeadline = Environment.TickCount64 + CosmicRecipeSelectionTimeoutMs;
            var selectionFailed = false;

            PreCrafting.Tasks.Add((() => PreCrafting.TaskSelectRecipe(recipe), TimeSpan.FromMilliseconds(500)));
            P.TM.Enqueue(() =>
            {
                if (PreCrafting.Tasks.Count == 0)
                    return true;

                if (Environment.TickCount64 < selectionDeadline)
                    return false;

                selectionFailed = true;
                PreCrafting.Tasks.Clear();
                var reason = $"無法在 {CosmicRecipeSelectionTimeoutMs / 1000} 秒內選中宇宙配方 {recipeId}。";
                CraftRequestFailures[recipeId] = reason;
                DuoLog.Error(reason);
                return true;
            }, CosmicRecipeSelectionTimeoutMs + 5_000, true, $"WaitingForCosmicRecipe:{recipeId}");
            P.TM.DelayNext(100);
            P.TM.Enqueue(() =>
            {
                if (selectionFailed)
                    return;

                var selectedRecipe = Operations.GetSelectedRecipeEntry();
                if (recipe.Number == 0 && (selectedRecipe == null || selectedRecipe->RecipeId != recipeId))
                {
                    var reason = $"宇宙製作手冊未選中配方 {recipeId}，已拒絕啟動製作。";
                    CraftRequestFailures[recipeId] = reason;
                    DuoLog.Error(reason);
                    return;
                }

                Endurance.IPCOverride = true;
                Endurance.RecipeID = recipeId;
                P.Config.CraftX = amount;
                P.Config.CraftingX = true;
                Endurance.ToggleEndurance(true);
            });
        }

        public static bool IsBusy()
        {
            return RaphaelCache.InProgressAny() || Endurance.Enable || CraftingListUI.Processing || P.TM.NumQueuedTasks > 0 || P.CTM.NumQueuedTasks > 0 || !(Crafting.CurState is Crafting.State.IdleBetween or Crafting.State.IdleNormal);
        }

        private static int GetRaphaelStatus(ushort recipeId)
            => CraftRequestFailures.ContainsKey(recipeId)
                ? (int)RaphaelCache.GenerationStatus.Failed
                : (int)RaphaelCache.GetGenerationStatus(recipeId);

        private static string GetRaphaelFailure(ushort recipeId)
            => CraftRequestFailures.TryGetValue(recipeId, out var reason)
                ? reason
                : RaphaelCache.GetFailure(recipeId);

        private static bool SetTemporarySolver(uint recipeId, string solverName)
        {
            if (!TryBuildCraft(recipeId, out var craft))
                return false;

            var selectedSolver = CraftingProcessor.GetAvailableSolversForRecipe(craft, false)
                .FirstOrDefault(x => string.Equals(x.Name, solverName, StringComparison.Ordinal));
            if (string.IsNullOrEmpty(selectedSolver.Name))
            {
                DuoLog.Error($"配方 {recipeId} 不支援求解器「{solverName}」。");
                return false;
            }

            var config = GetOrCreateRecipeConfig(recipeId);
            config.TempSolverType = selectedSolver.Def.GetType().FullName!;
            config.TempSolverFlavour = selectedSolver.Flavour;
            return true;
        }

        private static bool SetTemporaryFood(uint recipeId, uint itemId, bool hq)
        {
            if (!RecipeExists(recipeId))
                return false;
            if (itemId is not (RecipeConfig.Default or RecipeConfig.Disabled)
                && !ConsumableChecker.GetFood(true, hq).Any(x => x.Id == itemId))
            {
                DuoLog.Error($"物品 {itemId} 不是 Artisan 可用的{(hq ? " HQ" : " NQ")}製作食物。");
                return false;
            }

            var config = GetOrCreateRecipeConfig(recipeId);
            config.TempRequiredFood = itemId == RecipeConfig.Default ? null : itemId;
            config.TempRequiredFoodHQ = hq;
            return true;
        }

        private static bool SetTemporaryPotion(uint recipeId, uint itemId, bool hq)
        {
            if (!RecipeExists(recipeId))
                return false;
            if (itemId is not (RecipeConfig.Default or RecipeConfig.Disabled)
                && !ConsumableChecker.GetPots(true, hq).Any(x => x.Id == itemId))
            {
                DuoLog.Error($"物品 {itemId} 不是 Artisan 可用的{(hq ? " HQ" : " NQ")}製作藥水。");
                return false;
            }

            var config = GetOrCreateRecipeConfig(recipeId);
            config.TempRequiredPotion = itemId == RecipeConfig.Default ? null : itemId;
            config.TempRequiredPotionHQ = hq;
            return true;
        }

        private static void ClearTemporaryRecipeSettings(uint recipeId)
        {
            if (P.Config.RecipeConfigs.TryGetValue(recipeId, out var config))
                config.ClearTemporaryOverrides();
        }

        private static void ClearAllTemporarySettings()
        {
            foreach (var config in P.Config.RecipeConfigs.Values.ToArray())
                config.ClearTemporaryOverrides();
        }

        private static string[] GetAvailableSolvers(uint recipeId)
            => TryBuildCraft(recipeId, out var craft)
                ? CraftingProcessor.GetAvailableSolversForRecipe(craft, false)
                    .Where(x => !string.IsNullOrEmpty(x.Name))
                    .Select(x => x.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
                : [];

        private static uint[] GetAvailableFood(bool hq)
            => ConsumableChecker.GetFood(true, hq).Select(x => x.Id).Distinct().ToArray();

        private static uint[] GetAvailablePots(bool hq)
            => ConsumableChecker.GetPots(true, hq).Select(x => x.Id).Distinct().ToArray();

        private static RecipeConfig GetOrCreateRecipeConfig(uint recipeId)
        {
            var config = P.Config.RecipeConfigs.GetValueOrDefault(recipeId) ?? new RecipeConfig();
            P.Config.RecipeConfigs[recipeId] = config;
            return config;
        }

        private static bool RecipeExists(uint recipeId)
        {
            if (LuminaSheets.RecipeSheet!.ContainsKey(recipeId))
                return true;
            DuoLog.Error($"找不到配方 {recipeId}。");
            return false;
        }

        private static bool TryBuildCraft(uint recipeId, out CraftState craft)
        {
            craft = null!;
            if (!LuminaSheets.RecipeSheet!.TryGetValue(recipeId, out var recipe))
            {
                DuoLog.Error($"找不到配方 {recipeId}。");
                return false;
            }

            var job = (Job)((uint)Job.CRP + recipe.CraftType.RowId);
            craft = Crafting.BuildCraftStateForRecipe(CharacterStats.GetBaseStatsForClassHeuristic(job), job, recipe);
            return craft != null;
        }

        public enum ArtisanMode
        {
            None = 0,
            Endurance = 1,
            Lists = 2,
        }
    }
}
