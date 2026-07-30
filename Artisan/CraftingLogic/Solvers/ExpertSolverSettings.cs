using Artisan.CraftingLogic.CraftData;
using Artisan.RawInformation;
using Artisan.RawInformation.Character;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures.TextureWraps;
using ECommons.DalamudServices;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using System;
using static Artisan.RawInformation.AddonExtensions;
using System.Numerics;

namespace Artisan.CraftingLogic.Solvers;

public class ExpertSolverSettings
{
    public bool MaxIshgardRecipes;
    public bool UseReflectOpener;
    public bool MuMeIntensiveGood = true; // if true, we allow spending mume on intensive (400p) rather than rapid (500p) if good condition procs
    public bool MuMeIntensiveMalleable = false; // if true and we have malleable during mume, use intensive rather than hoping for rapid
    public bool MuMeIntensiveLastResort = true; // if true and we're on last step of mume, use intensive (forcing via H&S if needed) rather than hoping for rapid (unless we have centered)
    public bool MuMePrimedManip = false; // if true, allow using primed manipulation after veneration is up on mume
    public bool MuMeAllowObserve = false; // if true, observe rather than use actions during unfavourable conditions to conserve durability
    public int MuMeMinStepsForManip = 2; // if this or less rounds are remaining on mume, don't use manipulation under favourable conditions
    public int MuMeMinStepsForVene = 1; // if this or less rounds are remaining on mume, don't use veneration
    public int MidMinIQForHSPrecise = 10; // min iq stacks where we use h&s+precise; 10 to disable
    public bool MidBaitPliantWithObservePreQuality = true; // if true, when very low on durability and without manip active during pre-quality phase, we use observe rather than normal manip
    public bool MidBaitPliantWithObserveAfterIQ = true; // if true, when very low on durability and without manip active after iq has 10 stacks, we use observe rather than normal manip or inno+finnesse
    public bool MidPrimedManipPreQuality = true; // if true, allow using primed manipulation during pre-quality phase
    public bool MidPrimedManipAfterIQ = true; // if true, allow using primed manipulation during after iq has 10 stacks
    public bool MidKeepHighDuraUnbuffed = true; // if true, observe rather than use actions during unfavourable conditions to conserve durability when no buffs are active
    public bool MidKeepHighDuraVeneration = false; // if true, observe rather than use actions during unfavourable conditions to conserve durability when veneration is active
    public bool MidAllowVenerationGoodOmen = true; // if true, we allow using veneration during iq phase if we lack a lot of progress on good omen
    public bool MidAllowVenerationAfterIQ = true; // if true, we allow using veneration after iq is fully stacked if we still lack a lot of progress
    public bool MidAllowIntensiveUnbuffed = false; // if true, we allow spending good condition on intensive if we still need progress when no buffs are active
    public bool MidAllowIntensiveVeneration = false; // if true, we allow spending good condition on intensive if we still need progress when veneration is active
    public bool MidAllowPrecise = true; // if true, we allow spending good condition on precise touch if we still need iq
    public bool MidAllowSturdyPreсise = false; // if true,we consider sturdy+h&s+precise touch a good move for building iq
    public bool MidAllowCenteredHasty = true; // if true, we consider centered hasty touch a good move for building iq (85% reliability)
    public bool MidAllowSturdyHasty = true; // if true, we consider sturdy hasty touch a good move for building iq (50% reliability), otherwise we use combo
    public bool MidAllowGoodPrep = true; // if true, we consider prep touch a good move for finisher under good+inno+gs
    public bool MidAllowSturdyPrep = true; // if true, we consider prep touch a good move for finisher under sturdy+inno
    public bool MidGSBeforeInno = true; // if true, we start quality combos with gs+inno rather than just inno
    public bool MidFinishProgressBeforeQuality = false; // if true, at 10 iq we first finish progress before starting on quality
    public bool MidObserveGoodOmenForTricks = false; // if true, we'll observe on good omen where otherwise we'd use tricks on good
    public bool FinisherBaitGoodByregot = true; // if true, use careful observations to try baiting good byregot
    public bool EmergencyCPBaitGood = false; // if true, we allow spending careful observations to try baiting good for tricks when we really lack cp
	public bool RapidSynthYoloAllowed = true; // if false, expert crafting may lock up midway, so not good for AFK crafting. This yolo however is likely to fail the craft, so disabling gives opportunity for intervention
    public bool UseMaterialMiracle = false;
	public int MinimumStepsBeforeMiracle = 10;

    [NonSerialized]
    public IDalamudTextureWrap? expertIcon;

    public ExpertSolverSettings()
    {
        var tex = Svc.PluginInterface.UiBuilder.LoadUld("ui/uld/RecipeNoteBook.uld");
        expertIcon = tex?.LoadTexturePart("ui/uld/RecipeNoteBook_hr1.tex", 14);
    }

    public bool Draw()
    {
        ImGui.TextWrapped("高難度配方求解器並非標準求解器的替代方案，僅用於高難度配方。");
        if (expertIcon != null)
        {
            ImGui.TextWrapped("此求解器只適用於製作筆記中帶有");
            ImGui.SameLine();
            ImGui.Image(expertIcon.Handle, expertIcon.Size, new Vector2(0, 0), new Vector2(1, 1), new Vector4(0.94f, 0.57f, 0f, 1f));
            ImGui.SameLine();
            ImGui.TextWrapped("圖示的配方。");
        }
        bool changed = false;
        ImGui.Indent();
        if (ImGui.CollapsingHeader("起手設定"))
        {
            changed |= ImGui.Checkbox($"起手使用 {Skills.Reflect.NameOfAction()}，而非 {Skills.MuscleMemory.NameOfAction()}", ref UseReflectOpener);
            changed |= ImGui.Checkbox($"若為{Condition.Good.ToLocalizedString()}{ConditionString}，允許將 {Skills.MuscleMemory.NameOfAction()} 用於 {Skills.IntensiveSynthesis.NameOfAction()}（400%），而非 {Skills.RapidSynthesis.NameOfAction()}（500%）", ref MuMeIntensiveGood);
            changed |= ImGui.Checkbox($"在 {Skills.MuscleMemory.NameOfAction()} 期間遇到{Condition.Malleable.ToLocalizedString()}{ConditionString}時，使用 {Skills.HeartAndSoul.NameOfAction()}＋{Skills.IntensiveSynthesis.NameOfAction()}", ref MuMeIntensiveMalleable);
            changed |= ImGui.Checkbox($"在 {Skills.MuscleMemory.NameOfAction()} 最後一步且並非{Condition.Centered.ToLocalizedString()}{ConditionString}時，使用 {Skills.IntensiveSynthesis.NameOfAction()}（必要時以 {Skills.HeartAndSoul.NameOfAction()} 強制使用）", ref MuMeIntensiveLastResort);
            changed |= ImGui.Checkbox($"若 {Skills.Veneration.NameOfAction()} 已生效，在{Condition.Primed.ToLocalizedString()}{ConditionString}時使用 {Skills.Manipulation.NameOfAction()}", ref MuMePrimedManip);
        changed |= ImGui.Checkbox($"在不利的{ConditionString}時使用 {Skills.Observe.NameOfAction()}，避免以 {Skills.RapidSynthesis.NameOfAction()} 消耗{DurabilityString}", ref MuMeAllowObserve);
            ImGui.Text($"僅在 {Skills.MuscleMemory.NameOfAction()} 剩餘步數高於此值時允許使用 {Skills.Manipulation.NameOfAction()}");
            ImGui.PushItemWidth(250);
            changed |= ImGui.SliderInt("###MumeMinStepsForManip", ref MuMeMinStepsForManip, 0, 5);
            ImGui.Text($"僅在 {Skills.MuscleMemory.NameOfAction()} 剩餘步數高於此值時允許使用 {Skills.Veneration.NameOfAction()}");
            ImGui.PushItemWidth(250);
            changed |= ImGui.SliderInt("###MuMeMinStepsForVene", ref MuMeMinStepsForVene, 0, 5);
        }
        if (ImGui.CollapsingHeader("主要循環設定"))
        {
            ImGui.Text($"使用 {Skills.HeartAndSoul.NameOfAction()}＋{Skills.PreciseTouch.NameOfAction()} 所需的最低 {Buffs.InnerQuiet.NameOfBuff()} 層數（設為 10 可停用）");
            ImGui.PushItemWidth(250);
            changed |= ImGui.SliderInt($"###MidMinIQForHSPrecise", ref MidMinIQForHSPrecise, 0, 10);
            changed |= ImGui.Checkbox($"在 {Buffs.InnerQuiet.NameOfBuff()} 未達 10 層且{DurabilityString}偏低時，優先使用 {Skills.Observe.NameOfAction()}，而非在非{Condition.Pliant.ToLocalizedString()}{ConditionString}使用 {Skills.Manipulation.NameOfAction()}", ref MidBaitPliantWithObservePreQuality);
            changed |= ImGui.Checkbox($"在 {Buffs.InnerQuiet.NameOfBuff()} 達 10 層且{DurabilityString}偏低時，優先使用 {Skills.Observe.NameOfAction()}，而非在非{Condition.Pliant.ToLocalizedString()}{ConditionString}使用 {Skills.Manipulation.NameOfAction()}／{Skills.Innovation.NameOfAction()}＋{Skills.TrainedFinesse.NameOfAction()}", ref MidBaitPliantWithObserveAfterIQ);
            changed |= ImGui.Checkbox($"在 {Buffs.InnerQuiet.NameOfBuff()} 未達 10 層前，於{Condition.Primed.ToLocalizedString()}{ConditionString}使用 {Skills.Manipulation.NameOfAction()}", ref MidPrimedManipPreQuality);
            changed |= ImGui.Checkbox($"在 {Buffs.InnerQuiet.NameOfBuff()} 達 10 層後，若有足夠 CP 善用{DurabilityString}，於{Condition.Primed.ToLocalizedString()}{ConditionString}使用 {Skills.Manipulation.NameOfAction()}", ref MidPrimedManipAfterIQ);
            changed |= ImGui.Checkbox($"沒有增益時，允許在不利的{ConditionString}使用 {Skills.Observe.NameOfAction()}", ref MidKeepHighDuraUnbuffed);
            changed |= ImGui.Checkbox($"在 {Buffs.Veneration.NameOfBuff()} 期間，允許於不利的{ConditionString}使用 {Skills.Observe.NameOfAction()}", ref MidKeepHighDuraVeneration);
            changed |= ImGui.Checkbox($"在{Condition.GoodOmen.ToLocalizedString()}時，若仍缺少大量{ProgressString}（超過 {Skills.IntensiveSynthesis.NameOfAction()} 可完成的量），允許使用 {Skills.Veneration.NameOfAction()}", ref MidAllowVenerationGoodOmen);
            changed |= ImGui.Checkbox($"在 {Buffs.InnerQuiet.NameOfBuff()} 達 10 層後，若仍缺少大量{ProgressString}（超過 {Skills.RapidSynthesis.NameOfAction()} 可完成的量），允許使用 {Skills.Veneration.NameOfAction()}", ref MidAllowVenerationAfterIQ);
            changed |= ImGui.Checkbox($"沒有增益且需要更多{ProgressString}時，將{Condition.Good.ToLocalizedString()}{ConditionString}用於 {Skills.IntensiveSynthesis.NameOfAction()}", ref MidAllowIntensiveUnbuffed);
            changed |= ImGui.Checkbox($"在 {Skills.Veneration.NameOfAction()} 期間需要更多{ProgressString}時，將{Condition.Good.ToLocalizedString()}{ConditionString}用於 {Skills.IntensiveSynthesis.NameOfAction()}", ref MidAllowIntensiveVeneration);
            changed |= ImGui.Checkbox($"需要更多 {Buffs.InnerQuiet.NameOfBuff()} 層數時，將{Condition.Good.ToLocalizedString()}{ConditionString}用於 {Skills.PreciseTouch.NameOfAction()}", ref MidAllowPrecise);
            changed |= ImGui.Checkbox($"將{Condition.Sturdy.ToLocalizedString()}{ConditionString}的 {Skills.HeartAndSoul.NameOfAction()}＋{Skills.PreciseTouch.NameOfAction()} 視為累積 {Buffs.InnerQuiet.NameOfBuff()} 的有效操作", ref MidAllowSturdyPreсise);
            changed |= ImGui.Checkbox($"將{Condition.Centered.ToLocalizedString()}{ConditionString}的 {Skills.HastyTouch.NameOfAction()} 視為累積 {Buffs.InnerQuiet.NameOfBuff()} 的有效操作（成功率 85%，消耗 10 {DurabilityString}）", ref MidAllowCenteredHasty);
            changed |= ImGui.Checkbox($"將{Condition.Sturdy.ToLocalizedString()}{ConditionString}的 {Skills.HastyTouch.NameOfAction()} 視為累積 {Buffs.InnerQuiet.NameOfBuff()} 的有效操作（成功率 50%，消耗 5 {DurabilityString}）", ref MidAllowSturdyHasty);
            changed |= ImGui.Checkbox($"若{DurabilityString}足夠，將{Condition.Good.ToLocalizedString()}{ConditionString}＋{Buffs.Innovation.NameOfBuff()}＋{Buffs.GreatStrides.NameOfBuff()}期間的 {Skills.PreparatoryTouch.NameOfAction()} 視為有效操作", ref MidAllowGoodPrep);
            changed |= ImGui.Checkbox($"若{DurabilityString}足夠，將{Condition.Sturdy.ToLocalizedString()}{ConditionString}＋{Buffs.Innovation.NameOfBuff()}期間的 {Skills.PreparatoryTouch.NameOfAction()} 視為有效操作", ref MidAllowSturdyPrep);
            changed |= ImGui.Checkbox($"在 {Skills.Innovation.NameOfAction()}＋{QualityString}連段前使用 {Skills.GreatStrides.NameOfAction()}", ref MidGSBeforeInno);
            changed |= ImGui.Checkbox($"開始提升{QualityString}前先完成{ProgressString}", ref MidFinishProgressBeforeQuality);
        changed |= ImGui.Checkbox($"若原本會在{Condition.Good.ToLocalizedString()}{ConditionString}使用 {Skills.TricksOfTrade.NameOfAction()}，則於{Condition.GoodOmen.ToLocalizedString()}{ConditionString}使用 {Skills.Observe.NameOfAction()}", ref MidObserveGoodOmenForTricks);
			changed |= ImGui.Checkbox($"高難度求解器卡住時允許使用 {Skills.RapidSynthesis.NameOfAction()}。停用可能中斷完全自動製作，但半自動使用較安全", ref RapidSynthYoloAllowed);
        }
        ImGui.Unindent();
        changed |= ImGui.Checkbox("伊修加德重建配方盡量提高品質，而非只達到最高獎勵門檻", ref MaxIshgardRecipes);
        ImGuiComponents.HelpMarker("將盡量提高品質，以獲得更多蒼天街振興票。");
        changed |= ImGui.Checkbox($"收尾：使用 {Skills.CarefulObservation.NameOfAction()} 嘗試等待{Condition.Good.ToLocalizedString()}{ConditionString}，再使用 {Skills.ByregotsBlessing.NameOfAction()}", ref FinisherBaitGoodByregot);
        changed |= ImGui.Checkbox($"緊急：CP 極低時使用 {Skills.CarefulObservation.NameOfAction()} 嘗試等待{Condition.Good.ToLocalizedString()}{ConditionString}，再使用 {Skills.TricksOfTrade.NameOfAction()}", ref EmergencyCPBaitGood);
        changed |= ImGui.Checkbox($"宇宙探索中使用 {Skills.MaterialMiracle.NameOfAction()}", ref UseMaterialMiracle);
		ImGui.PushItemWidth(250);
        changed |= ImGui.SliderInt($"嘗試 {Skills.MaterialMiracle.NameOfAction()} 前至少執行的步數###MinimumStepsBeforeMiracle", ref MinimumStepsBeforeMiracle, 0, 20);
        if (ImGuiEx.ButtonCtrl("將高難度求解器設定重設為預設值"))
        {
            P.Config.ExpertSolverConfig = new();
            changed |= true;
        }
        return changed;
    }
}
