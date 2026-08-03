using Artisan.Autocraft;
using Artisan.CraftingLists;
using Artisan.CraftingLogic.Solvers;
using Artisan.GameInterop;
using Artisan.RawInformation;
using Artisan.RawInformation.Character;
using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.DalamudServices;
using ECommons.Logging;
using OtterGui;
using System;
using System.Collections.Generic;

namespace Artisan.IPC
{
    internal static class IPC
    {
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
            if (LuminaSheets.RecipeSheet!.TryGetFirst(x => x.Value.RowId == recipeId, out var recipe))
            {
                var recipeConfig = P.Config.RecipeConfigs.GetValueOrDefault(recipeId);
                var usesRaphael = recipeConfig?.SolverType.Contains("Raphael") == true;
                var expectedJobId = recipe.Value.CraftType.RowId + 8;
                if (usesRaphael && (uint)CharacterInfo.JobID == expectedJobId)
                {
                    var craft = Crafting.BuildCraftStateForRecipe(CharacterStats.GetCurrentStats(), CharacterInfo.JobID, recipe.Value);
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

        private static void QueueCraftX(Lumina.Excel.Sheets.Recipe recipe, ushort recipeId, int amount)
        {
            PreCrafting.Tasks.Add((() => PreCrafting.TaskSelectRecipe(recipe), TimeSpan.FromMilliseconds(500)));
            P.TM.Enqueue(() => PreCrafting.Tasks.Count == 0);
            P.TM.DelayNext(100);
            P.TM.Enqueue(() =>
            {
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

        private static int GetRaphaelStatus(ushort recipeId) => (int)RaphaelCache.GetGenerationStatus(recipeId);

        private static string GetRaphaelFailure(ushort recipeId) => RaphaelCache.GetFailure(recipeId);

        public enum ArtisanMode
        {
            None = 0,
            Endurance = 1,
            Lists = 2,
        }
    }
}
