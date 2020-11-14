using System;
using System.Data.Common;
using System.ComponentModel;
using Rage;
using AmbientAICallouts;
using AmbientAICallouts.API;
using LSPDFR_Functions = LSPD_First_Response.Mod.API.Functions;

namespace ShotsFired
{
    //Its very important to use these unless the AiCallout will not be compatible with the player callouts
    //Please use try catch blocks to get an error message when chrashing.


    public class ShotsFired : AiCallout
    {
        bool shootWhileDriving = true;//(new Random().Next(2) == 0);
        public override bool Setup()
        {
            //Code for setting the scene. return true when Succesfull. 
            //Important: please set a calloutDetailsString with Set_AiCallout_calloutDetailsString(String calloutDetailsString) to ensure that your callout has a something a civilian can report.
            //Example idea: Place a Damaged Vehicle infront of a Pole and place a swearing ped nearby.
            try
            {
                SceneInfo = "Shots Fired";
                location = World.GetNextPositionOnStreet(Unit.Position.Around2D(Functions.minimumAiCalloutDistance, Functions.maximumAiCalloutDistance));
                arrivalDistanceThreshold = 30f;
                calloutDetailsString = "CRIME_SHOTS_FIRED";
                SetupSuspects(1);                                                                                        //need to stay 1. more would result that in a callout the rest would flee.
                GameFiber.StartNew(delegate { 
                    for(int i= 0; i < 200; i++ )
                    {
                        foreach (var suspect in Suspects) { suspect.Tasks.TakeCoverFrom(location, 140000); 
                            if (suspect.Tasks.CurrentTaskStatus != Rage.TaskStatus.Preparing && suspect.Tasks.CurrentTaskStatus != Rage.TaskStatus.InProgress)
                            {
                                suspect.Tasks.TakeCoverFrom(Game.LocalPlayer.Character.Position, 13000);
                            }
                        }
                        GameFiber.Yield();
                    }
                }, "AAIC-ShotsFired- get in cover at setup fiber");

                return true;
            }
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
                    GameFiber.WaitWhile(() => Unit.Position.DistanceTo(location) >= 50f && Game.LocalPlayer.Character.Position.DistanceTo(location) >= 50f, 0);


                    if (playerRespondingInAdditon)
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
                    } 
                    else
                    {
                        Unit.IsSirenSilent = true;
                        Unit.TopSpeed = 10f;

                        if (shootWhileDriving)
                        {
                            GameFiber.SleepUntil(() => Unit.Position.DistanceTo(location) < arrivalDistanceThreshold + 20f /* && Unit.Speed <= 1*/, 30000);
                            //simple system to give tasks
                            foreach (var suspect in Suspects) 
                                suspect.Tasks.FireWeaponAt(Unit.Passengers[0], 15000, FiringPattern.BurstFire);
                            GameFiber.Sleep(800);                                                                                                                                              //EDIT HERE
                            Unit.Driver.Tasks.PerformDrivingManeuver( new Random().Next(2) == 0? VehicleManeuver.HandBrakeLeft : VehicleManeuver.HandBrakeRight );
                            GameFiber.SleepUntil(() => Unit.Speed <= 2, 4000);
                            OfficersLeaveVehicle(true);
                            foreach (var officer in UnitOfficers) officer.Tasks.TakeCoverFrom(location, 13000);
                            GameFiber.Sleep(4000);                                                                                                                                              //EDIT HERE
                            UnitCallsForBackup("AAIC-OfficerDown");
                        } else
                        {
                            GameFiber.SleepUntil(() => location.DistanceTo(Unit.Position) < arrivalDistanceThreshold + 5f /* && Unit.Speed <= 1*/, 30000);
                            OfficersLeaveVehicle(true);

                            foreach (var officer in UnitOfficers)
                            {
                                officer.Tasks.FollowNavigationMeshToPosition(location, MathHelper.ConvertDirectionToHeading(location), 1f);
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
                    }
                    
                    while (LSPD_First_Response.Mod.API.Functions.IsCalloutRunning() || Game.LocalPlayer.Character.Position.DistanceTo(location) < 40f ) { GameFiber.Sleep(4000); }
                }

                return true;
            }
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
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At End(): " + e);
                return false;
            }
        }

        private void TaskShooting()
        {

        }

    }
}