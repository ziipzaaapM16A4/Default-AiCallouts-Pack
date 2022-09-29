using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rage;
using LSPDFR_Functions = LSPD_First_Response.Mod.API.Functions;
using Functions = AmbientAICallouts.API.Functions;
using AmbientAICallouts.API;
using System.Runtime.CompilerServices;
using LSPD_First_Response.Mod.API;

namespace Fighting
{
    public class Fighting : AiCallout
    {
        private Version stpVersion = new Version("4.9.4.7");
        private bool isSTPRunning = false;
        bool startLoosingHealth = false;
        enum Estate { driving, parking, approaching, exploration, handling, requestingbackup };

        public override bool Setup()
        {
            try
            {
                SceneInfo = "Fighting";
                arrivalDistanceThreshold = 14f;
                CalloutDetailsString = "CRIME_ASSAULT";

                if (Helper.IsExternalPluginRunning("StopThePed", stpVersion)) isSTPRunning = true;
                Vector3 proposedPosition = Game.LocalPlayer.Character.Position.Around2D(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 15f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 15f);
                bool posFound = false;
                int trys = 0;
                bool demandPavement = true;
                Vector3 tmp = new Vector3();
                while (!posFound)
                {
                    Rage.Native.NativeFunction.Natives.GET_SAFE_COORD_FOR_PED<bool>(Game.LocalPlayer.Character.Position.Around2D(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f), demandPavement, out tmp, 16);
                    Location = tmp;
                    if (Functions.IsLocationAcceptedBySystem(Location))
                        posFound = true;

                    trys++;
                    if (trys >= 30) demandPavement = false;
                    if (trys >= 60) { LogTrivial_withAiC("ERROR: in AICallout object: At Setup(): unable to find safe coords for this event"); return false; }
                }

                Functions.SetupSuspects(MO, 2);

                Suspects[0].Tasks.FightAgainst(Suspects[1]);
                Suspects[1].Tasks.FightAgainst(Suspects[0]);
                LSPDFR_Functions.SetPedResistanceChance(Suspects[0], 0.35f);
                LSPDFR_Functions.SetPedResistanceChance(Suspects[1], 0.35f);

                GameFiber.StartNew(delegate {
                    try
                    {
                        while (!startLoosingHealth)
                        {
                            foreach (var suspect in Suspects)
                            {
                                try { suspect.Health = 200; } catch { }
                            }
                            if (!Suspects.Any(p => p)) break;
                            GameFiber.Sleep(1000);
                        }
                    }
                    catch { }
                });

                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At Setup(): " + e);
                return false;
            }
        }
        public override bool Process()
        {
            try
            {
                bool callactive = true;
                int arrivalCounter = 0;
                Estate status = Estate.driving;

                LHandle pursuit;


                while (callactive)
                {
                    //Pursuit - Any suspect in pursuit? -->> hunt felon
                    if ((pursuit = LSPDFR_Functions.GetActivePursuit()) != null && Suspects.Any(s => (s ? LSPDFR_Functions.IsPedInPursuit(s) : false)))
                    {
                        foreach (var cop in Units[0].UnitOfficers) { LSPDFR_Functions.AddCopToPursuit(pursuit, cop); }

                        if (!LSPDFR_Functions.IsPursuitCalledIn(pursuit) 
                        && Suspects.Any(s => (s ? Units[0].UnitOfficers.Any(ofc => ofc.DistanceTo(s) < 70f) : false))) 
                            LSPDFR_Functions.SetPursuitAsCalledIn(pursuit);

                        //aic.OnScene = true; //Missing OnScene detail
                        Units[0].UnitStatus = EUnitStatus.OnScene;
                        GameFiber.SleepWhile(() => LSPDFR_Functions.IsPursuitStillRunning(pursuit), 360000);
                        callactive = false; //LSPDFR Pursuit managed ab jetzt
                    }
                    //Normal response
                    else if (status == Estate.driving)
                    {
                        if (Units[0].PoliceVehicle.Position.DistanceTo(Location) < 100f) { status = Estate.parking; }
                        else if ((Helper.IsGamePaused() ? arrivalCounter : arrivalCounter++) > 130 * 2 /*500ms sleep*/) { Disregard(); callactive = false; } //Depending on the Sleep duration
                    }
                    //Arriving and Parking the Vehicle
                    else if (status == Estate.parking)
                    {
                        if (Units[0].PoliceVehicle.Position.DistanceTo(Location) < 40f)
                        {
                            Units[0].PoliceVehicle.IsSirenSilent = true;
                            Units[0].PoliceVehicle.TopSpeed = 12f;
                            OfficerReportOnScene(Units[0]);

                            GameFiber.SleepUntil(() => Units[0].PoliceVehicle.Position.DistanceTo(Location) < arrivalDistanceThreshold + 5f, 30000);
                            Units[0].PoliceVehicle.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
                            GameFiber.SleepUntil(() => Units[0].PoliceVehicle.Speed <= 1, 5000);
                            OfficersLeaveVehicle(Units[0], true);

                            startLoosingHealth = true;
                            status = Estate.approaching;
                        }
                    }
                    //Aproaching while suspects in combat
                    else if (status == Estate.approaching && Suspects.Any(s => s ? Helper.IsTaskActive(s, 343) : false))
                    {
                        LogTrivialDebug_withAiC($"INFO: Approaching by guns drawn due to combat");
                        foreach (var officer in Units[0].UnitOfficers) { officer.Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_PISTOL"), 30, true); }

                        while (Units[0].UnitOfficers[0].DistanceTo(Suspects[0]) > 6f || Helper.IsTaskActive(Units[0].UnitOfficers[0], 230) || (Units[0].UnitOfficers.Count > 1 ?
                            Units[0].UnitOfficers[1].DistanceTo(Suspects[1]) > 6f || Helper.IsTaskActive(Units[0].UnitOfficers[1], 230) : false))
                        {
                            for (int i = 0; (Units[0].UnitOfficers.Count == 1 ? i < 1 : i < 2); i++)
                            {
                                if (Helper.IsTaskActive(Units[0].UnitOfficers[i], 15))
                                    if (Units[0].UnitOfficers[i].DistanceTo(Suspects[i]) > 6f)
                                        Units[0].UnitOfficers[i].Tasks.GoToWhileAiming(Suspects[i].Position, Suspects[i].Position, 5f, 1f, false, FiringPattern.SingleShot);
                                    else
                                        Units[0].UnitOfficers[i].Tasks.AimWeaponAt(Suspects[i], 30000);
                            }
                            GameFiber.Yield();
                        }
                        status = Estate.exploration;
                    }
                    //Aproaching while suspects not in combat
                    else if (status == Estate.approaching) 
                    {
                        LogTrivialDebug_withAiC($"INFO: Approaching by walking due to no combat");
                        Helper.PedsAproachAndFaceUntilReached(Units[0].UnitOfficers, Game.LocalPlayer.Character.Position, 1.1f, 5f);
                        status = Estate.exploration;
                    }
                    //all arrested ->> officers leave
                    else if (status == Estate.exploration && Suspects.All(s => s ? LSPDFR_Functions.IsPedArrested(s) : false))
                    {
                        LogTrivial_withAiC($"INFO: Situation seems to be under control. Leaving because all suspects are arrested");
                        Game.DisplaySubtitle($"~b~Cop~w~: You seem to have this under control. " + (Units[0].UnitOfficers.Count > 1 ? "We" : "I") + "'ll head back then", 4000);
                        GameFiber.Sleep(5000);
                        EnterAndDismiss(Units[0]);

                    }
                    //arrested or in investigation ->> wait until completed
                    else if (status == Estate.exploration && Suspects.Any(s => s ? isPedNeededToBeWhatched(s) : false))
                    {
                        LogTrivial_withAiC($"INFO: Situation seems to be under control. waiting for Player to finish");
                        Helper.TurnPedToFace(Units[0].UnitOfficers[0], Suspects[0]);
                        if (Units[0].UnitOfficers.Count > 1) Helper.TurnPedToFace(Units[0].UnitOfficers[1], Suspects[1]);

                        Game.DisplaySubtitle($"~b~Cop~w~: You seem to have this under control. " + (Units[0].UnitOfficers.Count > 1 ? "We" : "I") + "'ll stay until you finished", 4000);
                        GameFiber.WaitWhile(() => Suspects.Any(s => s ? isPedNeededToBeWhatched(s) : false) && Suspects.All(s => s ? !LSPDFR_Functions.IsPedInPursuit(s) : true), 360000);

                        if (Suspects.All(s => s ? !LSPDFR_Functions.IsPedInPursuit(s) : true))
                        {
                            Game.DisplaySubtitle($"~b~Cop~w~: Alright. See ya", 4000);
                            GameFiber.Sleep(5000);
                            EnterAndDismiss(Units[0]);
                            callactive = false;
                        }
                    }
                    //aihandle (non of the above) --> use automated old code
                    else if (status == Estate.exploration)
                    {
                        LogTrivial_withAiC($"INFO: Player did not took control over the situation");
                        if (IsAiTakingCare()) {
                            status = Estate.handling;
                        } else   
                        {
                            status = Estate.requestingbackup;                                      //Callout
                        }

                    }
                    else if (status == Estate.handling)
                    {
                        LogTrivial_withAiC($"INFO: chose selfhandle path");

                        foreach (var suspect in Suspects) { if (suspect) suspect.Tasks.PutHandsUp(6000, Units[0].UnitOfficers[0]); }
                        GameFiber.Sleep(7000);

                        foreach (var suspect in Suspects)
                        {
                            if (suspect) {
                                suspect.Tasks.Flee(Units[0].UnitOfficers[0], 100f, 30000);
                                suspect.IsPersistent = false;
                            }
                        }

                        GameFiber.Sleep(5100);
                        EnterAndDismiss(Units[0]);

                        LogTrivial_withAiC($"INFO: Call Finished");
                    }
                    //Cops need Backup
                    else if (status == Estate.requestingbackup)
                    {
                        LogTrivial_withAiC($"INFO: choosed callout path");

                        switch (new Random().Next(0, 5))
                        {
                            case 0:
                                UnitCallsForBackup("AAIC-OfficerDown");
                                break;
                            case 1:
                                UnitCallsForBackup("AAIC-OfficerInPursuit");
                                break;
                            default:
                                UnitCallsForBackup("AAIC-OfficerRequiringAssistance");
                                break;
                        }
                        GameFiber.Sleep(15000);
                        while (LSPDFR_Functions.IsCalloutRunning()) { GameFiber.Sleep(11000); } //OLD: OfficerRequiringAssistance.finished or OfficerInPursuit.finished
                    }

                    GameFiber.Sleep(500);
                }

                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At Process(): " + e);
                End();
                return false;
            }
        }

        public override bool End()
        {
            try
            {
                startLoosingHealth = true;
                foreach (var sus in Suspects) { if (sus) { sus.IsPersistent = false; if (!isPedArrestedOrStoppedByAnyPlugin(sus)) sus.BlockPermanentEvents = false; } }
                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At End():" + e);
                return false;
            }
        }

        /// <summary>is Ped Arrested, Stopped but not Transported by Any Plugin</summary>
        private bool isPedNeededToBeWhatched(Ped ped) {

            if (LSPDFR_Functions.IsPedBeingGrabbed(ped) && !LSPDFR_Functions.IsPedBeingGrabbedByPlayer(ped)) return false;
            else if (LSPDFR_Functions.IsPedArrested(ped) || isPedStoppedByAnyPlugin(ped) || LSPDFR_Functions.IsPedGettingArrested(ped)) return true;
            return false;
        }

        private bool isPedArrestedOrStoppedByAnyPlugin(Ped ped) {return LSPDFR_Functions.IsPedArrested(ped) || LSPDFR_Functions.IsPedGettingArrested(ped) || isPedStoppedByAnyPlugin(ped);}

        private bool isPedStoppedByAnyPlugin(Ped ped) {return LSPDFR_Functions.IsPedStoppedByPlayer(ped) || (isSTPRunning ? STPPluginSupport.isPedStoppedBySTP(ped) : false);}
    }

    //STP
    internal class STPPluginSupport
    {
        internal static bool isPedGrabbedBySTP(Ped ped)
        {
            if (StopThePed.API.Functions.isPedGrabbed(ped)) return true; else return false;
        }

        internal static bool isPedStoppedBySTP(Ped ped)
        {
            if (StopThePed.API.Functions.isPedStopped(ped)) return true; else return false;
        }

        //Arrested apparently does not exist. STP probably uses LSPDFR to indicate arrested status
    }
}