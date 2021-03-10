using System;
using System.Data.Common;
using System.ComponentModel;
using Rage;
using Rage.Native;
using AmbientAICallouts;
using AmbientAICallouts.API;
using LSPDFR_Functions = LSPD_First_Response.Mod.API.Functions;
using System.Linq;

namespace ShotsFired
{
    public class ShotsFired : AiCallout
    {
        public override int UnitsNeeded => 3;

        bool shootWhileDriving = true;//(new Random().Next(2) == 0);
        Random randomizer = new Random();
        public override bool Setup()
        {
            //Code for setting the scene. return true when Succesfull. 
            //Important: please set a calloutDetailsString with Set_AiCallout_calloutDetailsString(String calloutDetailsString) to ensure that your callout has a something a civilian can report.
            //Example idea: Place a Damaged Vehicle infront of a Pole and place a swearing ped nearby.
            try
            {
                #region Plugin Details
                SceneInfo = "Shots Fired";
                CalloutDetailsString = "CRIME_SHOTS_FIRED";
                arrivalDistanceThreshold = 30f;
                #endregion

                #region Spawnpoint searching
                Vector3 roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                bool posFound = false;
                int trys = 0;
                while (!posFound && trys < 30)
                {
                    roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                    Vector3 irrelevant;
                    float heading = 0f;       //vieleicht guckt der MVA dann in fahrtrichtung der unit

                    NativeFunction.Natives.x240A18690AE96513<bool>(roadside.X, roadside.Y, roadside.Z, out roadside, 0, 3.0f, 0f);//GET_CLOSEST_VEHICLE_NODE

                    NativeFunction.Natives.xA0F8A7517A273C05<bool>(roadside.X, roadside.Y, roadside.Z, heading, out roadside); //_GET_ROAD_SIDE_POINT_WITH_HEADING
                    NativeFunction.Natives.xFF071FB798B803B0<bool>(roadside.X, roadside.Y, roadside.Z, out irrelevant, out heading, 0, 3.0f, 0f); //GET_CLOSEST_VEHICLE_NODE_WITH_HEADING //Find Side of the road.

                    Location = roadside;


                    if (Location.DistanceTo(Game.LocalPlayer.Character.Position) > AmbientAICallouts.API.Functions.minimumAiCalloutDistance
                     && Location.DistanceTo(Game.LocalPlayer.Character.Position) < AmbientAICallouts.API.Functions.maximumAiCalloutDistance)
                        posFound = true;
                    trys++;
                }
                #endregion

                SetupSuspects(1);  //need to stay 1. more would result that in a callout the rest would flee due to the way the - AAIC Backup requests-LSPFR Callouts work.

                #region Tasking Suspect
                GameFiber.StartNew(delegate {
                    try { 
                        for(int i= 0; i < 50; i++ )
                        {
                            foreach (var suspect in Suspects) { 
                                if (suspect)
                                {
                                    suspect.Tasks.TakeCoverFrom(Location, 140000); 
                                    if (suspect.Tasks.CurrentTaskStatus != Rage.TaskStatus.Preparing && suspect.Tasks.CurrentTaskStatus != Rage.TaskStatus.InProgress)
                                    {
                                        suspect.Tasks.TakeCoverFrom(Game.LocalPlayer.Character.Position, 13000);
                                    }
                                }
                            }
                            GameFiber.Yield();
                        }
                    }
                    catch (System.Threading.ThreadAbortException) { }
                    catch (Exception e)
                    {
                        LogTrivial_withAiC("ERROR: in AICallout: AAIC-ShotsFired - get in cover at setup fiber: " + e);
                    }
                }, "AAIC-ShotsFired - get in cover at setup fiber");
                #endregion

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
            //Code for processing the the scene. return true when Succesfull.
            //Example idea: Cops arrive; Getting out; Starring at suspects; End();
            try
            {
                if (!IsUnitInTime(100f, 130))  //if vehicle is never reaching its location
                {
                    Disregard();
                }
                else  //if vehicle is reaching its location
                {
                    GameFiber.WaitWhile(() => Unit.Position.DistanceTo(Location) >= 50f && Game.LocalPlayer.Character.Position.DistanceTo(Location) >= 50f, 25000);

                    if (playerRespondingInAdditon)                  //obsolete soon i guess
                    {
                        var firstShotDurration = 3000;
                        var pursuit = LSPDFR_Functions.CreatePursuit();
                        foreach (var suspect in Suspects)
                        {
                            if (new Random().Next(2) == 0) { suspect.Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_MICROSMG"), 200, true); } else { suspect.Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_PISTOL"), 200, true); }
                            LSPDFR_Functions.AddPedToPursuit(pursuit, suspect);

                            LSPDFR_Functions.SetPursuitDisableAIForPed(suspect, true);
                            suspect.Tasks.FireWeaponAt(Unit, firstShotDurration + 2000, FiringPattern.BurstFire);
                        }

                        foreach (var cop in UnitOfficers)
                        {
                            LSPDFR_Functions.AddCopToPursuit(pursuit, cop);
                        }

                        GameFiber.Sleep(firstShotDurration);

                        foreach (var suspect in Suspects)
                        {
                            LSPDFR_Functions.SetPursuitDisableAIForPed(suspect, false);
                            var attributes = LSPDFR_Functions.GetPedPursuitAttributes(suspect);
                            attributes.AverageFightTime = 1;
                        }
                        LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);
                        while (LSPD_First_Response.Mod.API.Functions.IsCalloutRunning() || Game.LocalPlayer.Character.Position.DistanceTo(Location) < 40f) { GameFiber.Sleep(4000); }
                    }
                    else
                    {

                        if (IsAiTakingCare())
                        {
                            //---------------------------------------------------- Temporary fix -----------------------------------------------------------
                            if (Suspects[0]) Suspects[0].Delete();
                            GameFiber.WaitWhile(() => Unit.Position.DistanceTo(Location) >= 40f, 25000);
                            Unit.IsSirenSilent = true;
                            Unit.TopSpeed = 12f;
                            OfficerReportOnScene();

                            GameFiber.SleepUntil(() => Location.DistanceTo(Unit.Position) < arrivalDistanceThreshold + 5f /* && Unit.Speed <= 1*/, 30000);
                            Unit.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
                            GameFiber.SleepUntil(() => Unit.Speed <= 1, 5000);
                            OfficersLeaveVehicle(true);

                            LogTrivialDebug_withAiC($"DEBUG: Go Look Around");
                            string[] anims = { "wait_idle_a", "wait_idle_b", "wait_idle_c" };
                            foreach (var officer in UnitOfficers) { officer.Tasks.FollowNavigationMeshToPosition(Location.Around(7f, 10f), Unit.Heading, 0.6f, 20f, 20000); }                       //ToHeading is useless
                            GameFiber.Sleep(12000);                                                                                                   //Static behavior. bad way                                   
                            for (int i = 0; i < UnitOfficers.Count; i++)
                            {
                                if (UnitOfficers[i]) UnitOfficers[i].Tasks.PlayAnimation(new AnimationDictionary("missmic_4premierejimwaitbef_prem"), anims[randomizer.Next(0, anims.Length)], 1f, AnimationFlags.RagdollOnCollision);
                                GameFiber.Sleep(2000);
                            }
                            GameFiber.SleepWhile(() => UnitOfficers[0].Tasks.CurrentTaskStatus == Rage.TaskStatus.InProgress || UnitOfficers[0].Tasks.CurrentTaskStatus == Rage.TaskStatus.Preparing, 7000);

                            LogTrivialDebug_withAiC($"DEBUG: PrankCallSpeech");
                            UnitOfficers[0].PlayAmbientSpeech("S_M_Y_FIREMAN_01_WHITE_FULL_01", "EMERG_PRANK_CALL", 0, SpeechModifier.Force);                                                                       //Not finished needs speech
                            GameFiber.Sleep(4000);
                            EnterAndDismiss(false);
                            //---------------------------------------------------- Temporary fix End---------------------------------------------------------
                        }
                        else
                        {

                            Unit.IsSirenSilent = true;
                            Unit.TopSpeed = 10f;

                            if (shootWhileDriving)
                            {
                                GameFiber.SleepUntil(() => Unit.Position.DistanceTo(Location) < arrivalDistanceThreshold + 20f /* && Unit.Speed <= 1*/, 30000);
                                //simple system to give tasks
                                foreach (var suspect in Suspects)
                                    suspect.Tasks.FireWeaponAt(Unit.Passengers[0], 15000, FiringPattern.BurstFire);
                                GameFiber.Sleep(800);                                                                                                                                              //EDIT HERE
                                Unit.Driver.Tasks.PerformDrivingManeuver(new Random().Next(2) == 0 ? VehicleManeuver.HandBrakeLeft : VehicleManeuver.HandBrakeRight);
                                GameFiber.SleepUntil(() => Unit.Speed <= 2, 4000);
                                OfficersLeaveVehicle(true);
                                foreach (var officer in UnitOfficers) officer.Tasks.TakeCoverFrom(Location, 13000);
                                GameFiber.Sleep(4000);                                                                                                                                              //EDIT HERE
                                if (new Random().Next(0, 2) == 0)
                                    UnitCallsForBackup("AAIC-OfficerDown");
                                else
                                    UnitCallsForBackup("AAIC-OfficerUnderFire");
                            }
                            else
                            {
                                GameFiber.SleepUntil(() => Location.DistanceTo(Unit.Position) < arrivalDistanceThreshold + 5f /* && Unit.Speed <= 1*/, 30000);
                                OfficersLeaveVehicle(true);

                                foreach (var officer in UnitOfficers)
                                {
                                    officer.Tasks.FollowNavigationMeshToPosition(Location, MathHelper.ConvertDirectionToHeading(Location), 1f);
                                }
                                GameFiber.Sleep(1800);

                                switch (new Random().Next(0, 3))
                                {
                                    case 0:
                                        UnitCallsForBackup("AAIC-OfficerDown");
                                        break;
                                    case 1:
                                        UnitCallsForBackup("AAIC-OfficerInPursuit");
                                        break;
                                    case 2:
                                        UnitCallsForBackup("AAIC-OfficerUnderFire");
                                        break;
                                }
                            }
                            while (LSPD_First_Response.Mod.API.Functions.IsCalloutRunning() || Game.LocalPlayer.Character.Position.DistanceTo(Location) < 40f) { GameFiber.Sleep(4000); }
                        }
                    }
                }

                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At Process(): " + e);
                return false;
            }
        }

        public override bool End()
        {
            //Code for finishing the the scene. return true when Succesfull.
            //Example idea: Cops getting back into their vehicle. drive away dismiss the rest. after 90 secconds delete if possible entitys that have not moved away.
            try
            {

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