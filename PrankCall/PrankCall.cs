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

namespace PrankCall
{
    public class PrankCall : AiCallout
    {
        private Random randomizer = new Random();
        private enum Estate { driving, onscenereport, parking, approaching, investigation }; 

        public override bool Setup()
        {
            try
            {
                SceneInfo = "Civilian in need of assistance";
                CalloutDetailsString = "CIV_ASSISTANCE";

                Vector3 proposedPosition = Game.LocalPlayer.Character.Position.Around2D(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 15f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 15f);
                bool posFound = false;
                int trys = 0;
                while (!posFound)
                {
                    Location = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                    if (Functions.IsLocationAcceptedBySystem(Location))
                        posFound = true;

                    trys++;
                    if (trys >= 30) { LogTrivial_withAiC("ERROR: in AICallout object: At Setup(): unable to find safe coords for this event"); return false; }
                }

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
                uint timeStamp= Game.GameTime;
                string[] anims = { "wait_idle_a", "wait_idle_b", "wait_idle_c" };
                Estate status = Estate.driving;
                int statusChild = 0;
                LHandle pursuit;


                while (callactive)
                {
                    //Pursuit
                    if ((pursuit = LSPDFR_Functions.GetActivePursuit()) != null)
                    {
                        LogTrivial_withAiC("PrankCall turned unexpectedly into an actuall Case. Starting Pursuit");
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
                        if (Units[0].PoliceVehicle.Position.DistanceTo(Location) < 70f) {timeStamp = Game.GameTime; status = Estate.parking; }
                        else if (timeStamp + 130 * 1000 < Game.GameTime) { Disregard(); callactive = false; }   //130 Sekunden
                    }
                    //Arriving and Parking the Vehicle
                    else if (status == Estate.parking)
                    {
                        if (statusChild == 0 && (Units[0].PoliceVehicle.Position.DistanceTo(Location) < 40f || timeStamp + 15 * 1000 < Game.GameTime))    //15 Sekunden
                        {
                            Units[0].PoliceVehicle.IsSirenSilent = true;
                            Units[0].PoliceVehicle.TopSpeed = 12f;
                            OfficerReportOnScene(Units[0]);

                            statusChild = 1;
                        }
                        else if (Location.DistanceTo(Units[0].PoliceVehicle.Position) < arrivalDistanceThreshold + 5f || timeStamp + 30 * 1000 < Game.GameTime) //15 Sekunden addiert
                        {
                            if (Units[0].PoliceVehicle.Speed > 1)
                                Units[0].PoliceVehicle.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
                            else{
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
                        foreach (var officer in Units[0].UnitOfficers)
                        {
                            if (!Helper.IsTaskActive(officer, 35))
                                Helper.FollowNavMeshToCoord(officer, Location.Around(7f, 10f), 0.6f, 20000, 5f, true);  //We use the simple variant and not the hard persistant variant because we cannot know if the target is maybe unreachable.
                        }

                        if (timeStamp + 9 * 1000 < Game.GameTime) {                                                    //after a while Lets sey they reached their point
                            timeStamp = Game.GameTime;
                            statusChild = 0;
                            status = Estate.investigation;
                        }
                    }
                    //Investigate
                    else if (status == Estate.investigation)
                    {
                        if (statusChild == 0 && timeStamp + 26 * 1000 > Game.GameTime) {
                            bool night = false;
                            int flashlightOfficerIndex = (Units[0].UnitOfficers.Count > 1 ? 1 : 0);

                            if (World.TimeOfDay >= new TimeSpan(22, 0, 0) || World.TimeOfDay <= new TimeSpan(5, 0, 0))
                            {
                                night = true;

                                if (Helper.IsTaskActive(Units[0].UnitOfficers[flashlightOfficerIndex], 15) && timeStamp + 25 * 1000 > Game.GameTime) { 
                                    Rage.Native.NativeFunction.Natives.TASK_START_SCENARIO_IN_PLACE(Units[0].UnitOfficers[flashlightOfficerIndex], "WORLD_HUMAN_SECURITY_SHINE_TORCH", 25000, true);
                                }
                                else if (timeStamp + 25 * 1000 < Game.GameTime)
                                {
                                    Units[0].UnitOfficers[flashlightOfficerIndex].Tasks.Clear();
                                }

                                Rage.Object prop = Rage.Native.NativeFunction.Natives.GET_CLOSEST_OBJECT_OF_TYPE<Rage.Object>(Units[0].UnitOfficers[flashlightOfficerIndex], 1.5f, -66965919, false, true, true);
                                if (prop != null ? prop.IsValid(): false) {
                                    Vector3 lightsource = prop.GetOffsetPositionRight(0.22f);
                                    Rage.Native.NativeFunction.Natives.DRAW_SHADOWED_SPOT_LIGHT(lightsource, lightsource - prop.GetOffsetPositionRight(-1f), 255, 255, 255, 100.0f, 1f, 1.0f, 13.0f, 0);
                                }
                            } 

                            for (int i = 0; i < Units[0].UnitOfficers.Count; i++)
                            {
                                if (Units[0].UnitOfficers[i] && (night ? i != flashlightOfficerIndex : true)) 
                                    if (Helper.IsTaskActive(Units[0].UnitOfficers[i], 15))
                                        Units[0].UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("missmic_4premierejimwaitbef_prem"), anims[randomizer.Next(0, anims.Length)], 1f, AnimationFlags.RagdollOnCollision);
                            }
                        }
                        else if (statusChild == 0) {
                            Units[0].UnitOfficers[0].PlayAmbientSpeech("S_M_Y_FIREMAN_01_WHITE_FULL_01", "EMERG_PRANK_CALL", 0, SpeechModifier.Force);
                            statusChild = 1;   
                        }
                        else if (timeStamp + 30 * 1000 < Game.GameTime) {
                            timeStamp = Game.GameTime;
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
                EnterAndDismiss(Units[0]);
                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At End(): " + e);
                return false;
            }
        }
    }
}