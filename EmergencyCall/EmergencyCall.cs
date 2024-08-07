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
using LSPD_First_Response.Mod.API;
using System.ComponentModel;

namespace EmergencyCall
{
    public class EmergencyCall : AiCallout
    {
        private Ped caller;
        private bool warrantForArrest;
        private Rage.Object notepad = null;
        private Random rand = new Random();
        private enum Estate { driving, parking, approaching, investigation, handling, pursuit };

        public override bool Setup() {
            try {
                SceneInfo = "Civilian in need of assistance";
                CalloutDetailsString = "CIV_ASSISTANCE";
                if (rand.Next(0, 2) == 0) { ResponseType = EResponseType.Code3; }
                else { ResponseType = EResponseType.Code2; }

                Vector3 roadside = new Vector3();
                Vector3 streetDirection = new Vector3();
                bool posFound = false;
                int trys = 0;
                bool demandPavement = true;
                while (!posFound)
                {
                    roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                    NativeFunction.Natives.xB61C8E878A4199CA<bool>(roadside, demandPavement, out roadside, 16); //GET_SAFE_COORD_FOR_PED
                    Location = roadside;

                    if (Functions.IsLocationAcceptedBySystem(Location) && Location != new Vector3(0,0,0))
                        posFound = true;

                    trys++;
                    if (trys == 30) { demandPavement = false; }
                    if (trys >= 60) { LogTrivial_withAiC("ERROR: in AICallout object: At Setup(): unable to find safe coords for this event"); return false; }
                }

                caller = new Ped(Location);
                warrantForArrest = LSPDFR_Functions.GetPersonaForPed(caller).Wanted;
                Rage.Native.NativeFunction.Natives.x240A18690AE96513<bool>(caller.Position, out streetDirection, 0, 3f, 0f); //GET_CLOSEST_VEHICLE_NODE
                Helper.TurnPedToFace(caller, streetDirection);
                GameFiber.Sleep(2500);
                caller.Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), "idle_c", 2f, AnimationFlags.Loop);

                return true;
            } catch (System.Threading.ThreadAbortException) { return false; } catch (Exception e) {
                LogTrivial_withAiC("ERROR: in AICallout object: At Setup(): " + e);
                return false;
            }
        }
        public override bool Process() {
            try {
                bool callactive = true;
                uint timeStamp = Game.GameTime;
                Estate status = Estate.driving;
                int statusChild = 0;
                int statusChild2 = 0;
                //      delegate typs
                //Action has no return value
                Func<Ped, bool> IsDoingNothing = (ofc) => Helper.IsTaskActive(ofc, 15);         //Func has a variable return value
                Func<Ped, bool> IsDoingUseScenarioTask = (ofc) => Helper.IsTaskActive(ofc, 118);   //Predicate has a bool return value
                Func<Ped, bool> IsDoingScriptedTask = (ofc) => Helper.IsTaskActive(ofc, 134);
                                                                                                //Eventhandler no retun value but as parameter can the Event can be given back

                LHandle pursuit;
                bool pursuitWasSelfInitiated = false;

                while (callactive) {

                    //Pursuit - Any suspect in pursuit? -->> hunt felon
                    if ((pursuit = LSPDFR_Functions.GetActivePursuit()) != null && Suspects.Any(s => (s ? LSPDFR_Functions.IsPedInPursuit(s) : false)) && !pursuitWasSelfInitiated) {
                        status = Estate.pursuit;
                    }


                    switch (status) {
                        case Estate.pursuit:
                            LogTrivial_withAiC("Investigation turned into a Chase. Starting Pursuit");
                            Units[0].PoliceVehicle.TopSpeed = 45f;
                            foreach (var cop in Units[0].UnitOfficers) { LSPDFR_Functions.AddCopToPursuit(pursuit, cop); }

                            if (!LSPDFR_Functions.IsPursuitCalledIn(pursuit)
                            && Suspects.Any(s => (s ? Units[0].UnitOfficers.Any(ofc => ofc.DistanceTo(s) < 70f) : false)))
                                LSPDFR_Functions.SetPursuitAsCalledIn(pursuit);

                            //aic.OnScene = true; //Missing OnScene detail
                            Units[0].UnitStatus = EUnitStatus.OnScene;
                            GameFiber.SleepWhile(() => LSPDFR_Functions.IsPursuitStillRunning(pursuit), 360000); //LSPDFR Pursuit managing now
                            callactive = false;
                            break;
                        case Estate.driving:
                            if (Units[0].PoliceVehicle.Position.DistanceTo(Location) < 70f) { timeStamp = Game.GameTime; status = Estate.parking; }
                            else if (timeStamp + (ResponseType == EResponseType.Code3 ? 130 : 260) * 1000 < Game.GameTime) { callactive = false; Disregard(); }//if vehicle is never reaching its location.
                            break;
                        case Estate.parking:
                            OfficersArrive();
                            status = Estate.approaching;
                            break;
                        case Estate.approaching:
                            Helper.CopsApproachAndFaceUntilReachedButStopOnPursuitInDistance(Units[0].UnitOfficers, caller.Position, 1f, 4f, 60000);
                            status = Estate.investigation;
                            break;
                        case Estate.investigation:
                            switch (statusChild) {
                                case 0: //Posisioning and looking at caller
                                    foreach (var ofc in Units[0].UnitOfficers) { Helper.TurnPedToFace(ofc, caller); }
                                    Helper.TurnPedToFace(caller, Units[0].UnitOfficers[0]);
                                    GameFiber.Sleep(2000);
                                    statusChild = 1;
                                    break;
                                case 1: //Talking to caller
                                    switch (statusChild2) {
                                        case 0:
                                            NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[0], "CODE_HUMAN_MEDIC_TIME_OF_DEATH", 40000, true);  //TASK_START_SCENARIO_IN_PLACE
                                            if (Units[0].UnitOfficers.Count > 1)
                                                NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[1], "CODE_HUMAN_POLICE_INVESTIGATE", 40000, true);  //TASK_START_SCENARIO_IN_PLACE
                                            caller.Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), "idle_c", 2f, AnimationFlags.Loop);
                                            timeStamp = Game.GameTime;
                                            statusChild2 = 1;
                                            break;
                                        case 1:
                                            if (timeStamp + 45000 > Game.GameTime) { //ReTask if not Tasked
                                                if (!Helper.IsTaskActive(Units[0].UnitOfficers[0], 118)) {
                                                    NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[0], "CODE_HUMAN_MEDIC_TIME_OF_DEATH", 40000, true);  //TASK_START_SCENARIO_IN_PLACE
                                                }
                                                if (Units[0].UnitOfficers.Count > 1) {
                                                    if (!Helper.IsTaskActive(Units[0].UnitOfficers[1], 118)) {
                                                        NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[1], "CODE_HUMAN_POLICE_INVESTIGATE", 40000, true);  //TASK_START_SCENARIO_IN_PLACE
                                                    }
                                                }
                                                if (!Helper.IsTaskActive(caller, 134)) {
                                                    caller.Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), "idle_c", 2f, AnimationFlags.Loop);
                                                }
                                            } else {  //Exit Task
                                                if (Helper.IsTaskActive(Units[0].UnitOfficers[0], 118)) {
                                                    Units[0].UnitOfficers[0].Tasks.Clear();
                                                }
                                                if (Units[0].UnitOfficers.Count > 1) {
                                                    if (Helper.IsTaskActive(Units[0].UnitOfficers[1], 118)) {
                                                        Units[0].UnitOfficers[1].Tasks.Clear();
                                                    }
                                                }
                                                timeStamp = Game.GameTime;
                                                statusChild2 = 2;
                                            }
                                            break;
                                        case 2:
                                            if (!Units[0].UnitOfficers.Any(IsDoingUseScenarioTask)) { //if no cop is still doing scenario stuff
                                                timeStamp = Game.GameTime;
                                                statusChild2 = 3;
                                            }
                                            break;
                                        case 3:
                                            if (warrantForArrest) {
                                                status = Estate.handling;
                                            }
                                            else {
                                                statusChild = 2;
                                            }
                                            break;

                                    }
                                    break;
                                case 2: //Releaseing caller
                                    if (caller) { caller.Tasks.Clear(); caller.Dismiss(); }
                                    if (Units[0].UnitOfficers.Count > 1 && rand.Next(2) == 1) { statusChild = 3; statusChild2 = 0; }
                                    else { callactive = false; }
                                    break;
                                case 3:  //Talking to each other (sometimes)
                                    switch (statusChild2) {
                                        case 0:
                                            timeStamp = Game.GameTime;
                                            statusChild2 = 1;
                                            break;
                                        case 1:
                                            if (timeStamp + 1800 < Game.GameTime) {
                                                timeStamp = Game.GameTime;
                                                statusChild2 = 2;
                                            }
                                            break;
                                        case 2:
                                            Helper.TurnPedToFace(Units[0].UnitOfficers[0], Units[0].UnitOfficers[1]);
                                            Helper.TurnPedToFace(Units[0].UnitOfficers[1], Units[0].UnitOfficers[0]);
                                            timeStamp = Game.GameTime;
                                            statusChild2 = 3;
                                            break;
                                        case 3:
                                            if (timeStamp + 3000 < Game.GameTime) {
                                                timeStamp = Game.GameTime;
                                                statusChild2 = 4;
                                            }
                                            break;
                                        case 4:
                                            Rage.Native.NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[0], "WORLD_HUMAN_HANG_OUT_STREET", 10000, true); //TASK_START_SCENARIO_IN_PLACE
                                            Rage.Native.NativeFunction.Natives.x142A02425FF02BD9(Units[0].UnitOfficers[1], "WORLD_HUMAN_HANG_OUT_STREET", 10000, true); //TASK_START_SCENARIO_IN_PLACE
                                            timeStamp = Game.GameTime;
                                            statusChild2 = 5;
                                            break;
                                        case 5:
                                            if (timeStamp + 10000 < Game.GameTime) {
                                                timeStamp = Game.GameTime;
                                                callactive = false;
                                            }
                                            break;
                                    }
                                    break;
                            }
                            break;
                        case Estate.handling: //if caller is suspect, then arrest him
                            pursuitWasSelfInitiated = true;
                            var Arrest = LSPDFR_Functions.CreatePursuit();
                            LSPDFR_Functions.SetPursuitInvestigativeMode(Arrest, true);
                            foreach (var ofc in Units[0].UnitOfficers)
                                if (ofc) LSPDFR_Functions.AddCopToPursuit(Arrest, ofc);
                            if (caller) LSPDFR_Functions.AddPedToPursuit(Arrest, caller);
                            while (LSPDFR_Functions.IsPursuitStillRunning(Arrest)) { GameFiber.Sleep(500); } //ToDo: Bad Practice. Blocks the thread. Find a better way to do this.
                            break;
                    }
                    GameFiber.Sleep(800);
                }
            } catch (System.Threading.ThreadAbortException) { if (caller) caller.Delete(); return false; } catch (Exception e) {
                if (caller)
                    caller.Delete();
                LogTrivial_withAiC("ERROR: in AICallout object: At Process(): " + e);
                AbortCode();
                return false;
            }
            return true;
        }

        private void OfficersArrive() {
            GameFiber.WaitWhile(() => Units[0].PoliceVehicle.Position.DistanceTo(Location) >= 40f, 25000);
            Units[0].PoliceVehicle.IsSirenSilent = true;
            Units[0].PoliceVehicle.TopSpeed = 12f;
            OfficerReportOnScene(Units[0]);

            GameFiber.SleepUntil(() => Location.DistanceTo(Units[0].PoliceVehicle.Position) < arrivalDistanceThreshold + 5f /* && Units[0].PoliceVehicle.Speed <= 1*/, 30000);
            Units[0].PoliceVehicle.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
            GameFiber.SleepUntil(() => Units[0].PoliceVehicle.Speed <= 1, 5000);
            OfficersLeaveVehicle(Units[0], false);
        }

        public override bool End() {
            try {
                if (caller)
                    caller.IsPersistent = false;
                EnterAndDismiss(Units[0]);
                return true;
            } catch (System.Threading.ThreadAbortException) { return false; } catch (Exception e) {
                LogTrivial_withAiC("ERROR: in AICallout object: At End(): " + e);
                return false;
            }
        }
    }
}