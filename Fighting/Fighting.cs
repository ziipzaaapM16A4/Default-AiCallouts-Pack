using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rage;
using Rage.Native;
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
        enum Estate { driving, parking, approaching, investigation, handling, requestingbackup };

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
                    Rage.Native.NativeFunction.Natives.xB61C8E878A4199CA<bool>(Game.LocalPlayer.Character.Position.Around2D(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f), demandPavement, out tmp, 16); //GET_SAFE_COORD_FOR_PED
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
                uint timeStamp = Game.GameTime;
                Estate status = Estate.driving;
                int statusChild = 0;

                LHandle pursuit;


                while (callactive)
                {
                    //Pursuit - Any suspect in pursuit? -->> hunt felon
                    if ((pursuit = LSPDFR_Functions.GetActivePursuit()) != null && Suspects.Any(s => (s ? LSPDFR_Functions.IsPedInPursuit(s) : false)))
                    {
                        LogTrivial_withAiC("Fighting turned into a Chase. Starting Pursuit");
                        Units[0].PoliceVehicle.TopSpeed = 45f;
                        foreach (var cop in Units[0].UnitOfficers) { LSPDFR_Functions.AddCopToPursuit(pursuit, cop); }

                        if (!LSPDFR_Functions.IsPursuitCalledIn(pursuit) 
                        && Suspects.Any(s => (s ? Units[0].UnitOfficers.Any(ofc => ofc.DistanceTo(s) < 70f) : false))) 
                            LSPDFR_Functions.SetPursuitAsCalledIn(pursuit);

                        //aic.OnScene = true; //Missing OnScene detail
                        Units[0].UnitStatus = EUnitStatus.OnScene;
                        GameFiber.SleepWhile(() => LSPDFR_Functions.IsPursuitStillRunning(pursuit), 360000); //LSPDFR Pursuit managed ab jetzt
                        callactive = false;
                    }
                    //Normal response
                    else if (status == Estate.driving)
                    {
                        if (Units[0].PoliceVehicle.Position.DistanceTo(Location) < 70f) { timeStamp = Game.GameTime; status = Estate.parking; }
                        else if (timeStamp + 130 * 1000 < Game.GameTime) { Disregard(); callactive = false; }
                    }
                    //Arriving and Parking the Vehicle
                    else if (status == Estate.parking)
                    {
                        if (statusChild == 0 && (Units[0].PoliceVehicle.Position.DistanceTo(Location) < 40f || timeStamp + 15 * 1000 < Game.GameTime))    //15 Sec
                        {
                            Units[0].PoliceVehicle.IsSirenSilent = true;
                            Units[0].PoliceVehicle.TopSpeed = 12f;
                            OfficerReportOnScene(Units[0]);
                            timeStamp = Game.GameTime;
                            statusChild = 1;
                        }
                        else if (Location.DistanceTo(Units[0].PoliceVehicle.Position) < arrivalDistanceThreshold + 5f || timeStamp + 15 * 1000 < Game.GameTime) //15 Sec
                        {
                            if (Units[0].PoliceVehicle.Speed > 1)
                                Units[0].PoliceVehicle.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
                            else
                            {
                                OfficersLeaveVehicle(Units[0], true);                                                   //Sleeping Fiber
                                timeStamp = Game.GameTime;
                                statusChild = 0;
                                status = Estate.approaching;
                            }
                        }
                    }
                    //Aproaching
                    else if (status == Estate.approaching)
                    {
                        //Aproaching while suspects in combat
                        if (Suspects.Any(s => s ? Helper.IsTaskActive(s, 343) : false))
                        {
                            //LogTrivialDebug_withAiC($"INFO: Approaching by guns drawn due to combat");

                            //while (Units[0].UnitOfficers[0].DistanceTo(Suspects[0]) > 6f || Helper.IsTaskActive(Units[0].UnitOfficers[0], 230) || (Units[0].UnitOfficers.Count > 1 ?
                            //       Units[0].UnitOfficers[1].DistanceTo(Suspects[1]) > 6f || Helper.IsTaskActive(Units[0].UnitOfficers[1], 230) : false))
                            if ((Units[0].UnitOfficers[0].DistanceTo(Suspects[0]) > 6f || Helper.IsTaskActive(Units[0].UnitOfficers[0], 230) || (Units[0].UnitOfficers.Count > 1 ?
                                Units[0].UnitOfficers[1].DistanceTo(Suspects[1]) > 6f || Helper.IsTaskActive(Units[0].UnitOfficers[1], 230) : false)
                                ) && timeStamp + 60 * 1000 > Game.GameTime)
                            {
                                foreach (var officer in Units[0].UnitOfficers) { officer.Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_PISTOL"), 30, true); }
                                for (int i = 0; (Units[0].UnitOfficers.Count == 1 ? i < 1 : i < 2); i++)
                                {
                                    if (Helper.IsTaskActive(Units[0].UnitOfficers[i], 15))
                                        if (Units[0].UnitOfficers[i].DistanceTo(Suspects[i]) > 6f)
                                            Units[0].UnitOfficers[i].Tasks.GoToWhileAiming(Suspects[i].Position, Suspects[i].Position, 5f, 1f, false, FiringPattern.SingleShot);
                                        else
                                            Units[0].UnitOfficers[i].Tasks.AimWeaponAt(Suspects[i].Position, 30000);
                                }
                                //GameFiber.Yield();
                            } else
                            {
                                foreach (var officer in Units[0].UnitOfficers) { officer.Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_UNARMED"), 1, true); }
                                timeStamp = Game.GameTime;
                                statusChild = 0;
                                status = Estate.investigation;
                            }
                        }
                        //Aproaching while suspects not in combat
                        else
                        {
                            LogTrivialDebug_withAiC($"INFO: Approaching by walking due to no combat");
                            Helper.PedsAproachAndFaceUntilReached(Units[0].UnitOfficers, Suspects[0].Position, 1.1f, 6f);                   //Sleeping Fiber. Blocking Thread
                            timeStamp = Game.GameTime;
                            statusChild = 0;
                            status = Estate.investigation;
                        }

                    }
                    //investigation
                    else if (status == Estate.investigation)
                    {
                        if (statusChild == 0) {
                            if (Suspects.All(s => s ? LSPDFR_Functions.IsPedArrested(s) : false))
                            { statusChild = 1; LogTrivial_withAiC($"INFO: Situation seems to be under control. Leaving because all suspects are arrested"); }
                            else if (Suspects.Any(s => s ? isPedNeededToBeWhatched(s) : false))
                            { statusChild = 2; LogTrivial_withAiC($"INFO: Situation seems to be under control. Waiting for Player to finish"); }
                            else
                            { statusChild = 3; LogTrivial_withAiC($"INFO: Player did not took control over the situation"); } 
                        }

                        //all arrested ->> officers leave
                        if (statusChild == 1 || 10 < statusChild && statusChild < 20) {
                            if (statusChild == 1) { statusChild = 12; }
                            else if (statusChild == 12)
                            {
                                NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[0], "CODE_HUMAN_POLICE_INVESTIGATE", 7000, true);  //TASK_START_SCENARIO_IN_PLACE
                                NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[1], "CODE_HUMAN_POLICE_INVESTIGATE", 7000, true);  //TASK_START_SCENARIO_IN_PLACE
                                statusChild = 13;
                            }
                            else if (statusChild == 13 && timeStamp + 2 * 1000 < Game.GameTime)  //2 Sec
                            {
                                Game.DisplaySubtitle($"~b~Cop~w~: You seem to have this under control. " + (Units[0].UnitOfficers.Count > 1 ? "We" : "I") + "'ll head back then", 4000);
                                timeStamp = Game.GameTime;
                                statusChild = 14;
                            }
                            else if (statusChild == 14 && timeStamp + 5 * 1000 < Game.GameTime) //5 Sec
                            {
                                EnterAndDismiss(Units[0]);

                                timeStamp = Game.GameTime;
                                statusChild = 0;
                                callactive = false;
                            }
                        }
                        //arrested or in investigation ->> wait until completed
                        else if (statusChild == 2 || 20 < statusChild && statusChild < 30)
                        {
                            if (statusChild == 2 && timeStamp + 2 * 1000 < Game.GameTime)
                            {
                                Game.DisplaySubtitle($"~b~Cop~w~: You seem to have this under control. " + (Units[0].UnitOfficers.Count > 1 ? "We" : "I") + "'ll stay until you finished", 5000);
                                timeStamp = Game.GameTime;
                                statusChild = 22;
                            }
                            else if (statusChild == 22 && timeStamp + 2 * 1000 < Game.GameTime)
                            {
                                Helper.TurnPedToFace(Units[0].UnitOfficers[0], Suspects[0]);
                                if (Units[0].UnitOfficers.Count > 1) Helper.TurnPedToFace(Units[0].UnitOfficers[1], Suspects[1]);
                                timeStamp = Game.GameTime;
                                statusChild = 23;
                            }
                            else if (statusChild == 23 && timeStamp + 2.5 * 1000 < Game.GameTime)
                            {
                                NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[0], "CODE_HUMAN_POLICE_INVESTIGATE", 600000, true);  //TASK_START_SCENARIO_IN_PLACE
                                NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[1], "CODE_HUMAN_POLICE_INVESTIGATE", 600000, true);  //TASK_START_SCENARIO_IN_PLACE
                                timeStamp = Game.GameTime;
                                statusChild = 24;
                            }
                            else if (statusChild == 24)
                            {
                                if (!(Suspects.Any(s => s ? isPedNeededToBeWhatched(s) : false) && Suspects.All(s => s ? !LSPDFR_Functions.IsPedInPursuit(s) : true))
                                    || timeStamp + 604 * 1000 < Game.GameTime)
                                {
                                    timeStamp = Game.GameTime;
                                    statusChild = 25;
                                }
                            }
                            else if (statusChild == 25)
                            {
                                if (Suspects.All(s => s ? !LSPDFR_Functions.IsPedInPursuit(s) : true))
                                {
                                    Game.DisplaySubtitle($"~b~Cop~w~: Alright. See ya", 4000);
                                    statusChild = 26;
                                }
                            } else if (statusChild == 26 && timeStamp + 5 * 1000 < Game.GameTime)
                            {
                                timeStamp = Game.GameTime;
                                statusChild = 0;
                                callactive = false;
                                EnterAndDismiss(Units[0]);
                            }
                        }
                        //aihandle (non of the above) --> use automated old code
                        else if (statusChild == 3)
                        {
                            if (IsAiTakingCare())
                            {
                                timeStamp = Game.GameTime;
                                statusChild = 0;
                                status = Estate.handling;
                            }
                            else
                            {
                                timeStamp = Game.GameTime;
                                statusChild = 0;
                                status = Estate.requestingbackup;                                      //Callout
                            }
                        }


                    }
                    //Selfhandle
                    else if (status == Estate.handling)
                    {
                        if (statusChild == 0)
                        {
                            LogTrivial_withAiC($"INFO: Chose selfhandle path");
                            NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[0], "WORLD_HUMAN_COP_IDLES", 12000, true);  //TASK_START_SCENARIO_IN_PLACE
                            NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[1], "CODE_HUMAN_POLICE_INVESTIGATE", 12000, true);  //TASK_START_SCENARIO_IN_PLACE
                            foreach (var suspect in Suspects) { if (suspect) suspect.Tasks.PutHandsUp(6000, Units[0].UnitOfficers[0]); }
                            statusChild = 1;
                        }
                        else if (statusChild == 1 && timeStamp + 7 * 1000 < Game.GameTime)
                        {
                            foreach (var suspect in Suspects)
                            {
                                if (suspect) {
                                    suspect.Tasks.Flee(Units[0].UnitOfficers[0], 100f, 30000);
                                    suspect.IsPersistent = false;
                                }
                            }
                            statusChild = 2;
                        }
                        else if (statusChild == 2 && timeStamp + 5.1 * 1000 < Game.GameTime)
                        { 
                            EnterAndDismiss(Units[0]);

                            callactive = false;
                            LogTrivial_withAiC($"INFO: Call Finished");
                        }
                    }
                    //Cops need Backup
                    else if (status == Estate.requestingbackup)
                    {
                        if (statusChild == 0)
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
                            statusChild = 1;
                        }
                        else if (statusChild == 1 && timeStamp + 15 * 1000 < Game.GameTime)
                        {
                            if (LSPDFR_Functions.IsCalloutRunning())                                                        //usually it only checks every 11 Seconds
                                callactive = false;
                        }
                    }

                    GameFiber.Yield();
                }
                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At Process(): " + e);
                AbortCode();
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