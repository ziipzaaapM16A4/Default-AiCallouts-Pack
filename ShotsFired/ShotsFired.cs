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

        bool playerInvolved = false;
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
                while (!posFound)
                {
                    //roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                    //Vector3 irrelevant;
                    //float heading = 0f;       //vieleicht guckt der MVA dann in fahrtrichtung der unit

                    //NativeFunction.Natives.x240A18690AE96513<bool>(roadside.X, roadside.Y, roadside.Z, out roadside, 0, 3.0f, 0f);//GET_CLOSEST_VEHICLE_NODE

                    //NativeFunction.Natives.xA0F8A7517A273C05<bool>(roadside.X, roadside.Y, roadside.Z, heading, out roadside); //_GET_ROAD_SIDE_POINT_WITH_HEADING
                    //NativeFunction.Natives.xFF071FB798B803B0<bool>(roadside.X, roadside.Y, roadside.Z, out irrelevant, out heading, 0, 3.0f, 0f); //GET_CLOSEST_VEHICLE_NODE_WITH_HEADING //Find Side of the road.

                    NativeFunction.Natives.GET_SAFE_COORD_FOR_PED<bool>(roadside, true, out roadside, 16);
                    Location = roadside;


                    if (Location.DistanceTo(Game.LocalPlayer.Character.Position) > AmbientAICallouts.API.Functions.minimumAiCalloutDistance
                     && Location.DistanceTo(Game.LocalPlayer.Character.Position) < AmbientAICallouts.API.Functions.maximumAiCalloutDistance)
                        posFound = true;

                    trys++;
                    if (trys >= 30) return false;
                }
                #endregion

                Functions.SetupSuspects(MO, 1);  //need to stay 1. more would result that in a callout the rest would flee due to the way the - AAIC Backup requests-LSPFR Callouts work.

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
                bool anyUnitOnScene = false;

                foreach (var unit in Units)
                {
                    GameFiber.StartNew(delegate{
                        var tmpUnit = unit;
                        try {
                            if (!IsUnitInTime(tmpUnit, arrivalDistanceThreshold + 40f, 130))  //if vehicle is never reaching its location
                            {
                                Disregard();
                            } else
                            {
                                anyUnitOnScene = true;
                            }
                        } catch { }
                    },"[AAIC] [Shotsfired] Fiber: Checking wether units even arrive");
                    GameFiber.Yield();
                }

                GameFiber.WaitWhile(() => !anyUnitOnScene && Game.LocalPlayer.Character.Position.DistanceTo(Location) >= arrivalDistanceThreshold + 40f, 25000);

                bool suspectalive = true;
                bool someoneSpottedSuspect = false;
                bool playerSpottedSuspect = false;
                bool suspectflees = false;
                int suspectInvalidCounter = 0;
                LSPD_First_Response.Mod.API.LHandle pursuit = LSPDFR_Functions.CreatePursuit();

                LSPDFR_Functions.SetPursuitCopsCanJoin(pursuit, false);
                LSPDFR_Functions.SetPursuitAsCalledIn(pursuit, false);
                LSPDFR_Functions.AddPedToPursuit(pursuit, Suspects[0]);
                LSPDFR_Functions.SetPursuitDisableAIForPed(Suspects[0], true);
                var attributes = LSPDFR_Functions.GetPedPursuitAttributes(Suspects[0]);
                attributes.AverageFightTime = 1;
                if (new Random().Next(2) == 0) { Suspects[0].Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_MICROSMG"), 200, true); } else { Suspects[0].Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_PISTOL"), 200, true); }



                while (suspectalive)
                {
                    if (!Suspects[0])//!Functions.EntityValidityChecks(MO, false))
                    {
                        if (suspectInvalidCounter % 30 == 0) LogTrivialDebug_withAiC("WARNING: Suspect is currently Invalid. Cannot process Script.");

                        if (suspectInvalidCounter > 100)    //sleep ist 100 ms also mal 100 = 10 sekunden.
                        {

                            LogTrivial_withAiC("ERROR: in AiCallout object: At Process(): Suspect entity was too long invalid while having them on persistent. Aborting Callout");
                            CleanUpEntitys();
                            return false;
                        }

                        suspectInvalidCounter++;
                    } else
                    {
                        suspectInvalidCounter = 0;
                        //-------------------------------------- Player -----------------------------------------------------
                        #region Officer Tasks
                        //If Player is near enough to the scene and other units spotted the suspect already => auto pursuit
                        if (someoneSpottedSuspect 
                            && !playerSpottedSuspect
                            && (Game.LocalPlayer.Character.DistanceTo(Suspects[0]) < 65f || Game.LocalPlayer.Character.DistanceTo(Location) <= arrivalDistanceThreshold)
                            )
                            LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);


                        //Player Spottet Suspect
                        if (!someoneSpottedSuspect && !playerSpottedSuspect)
                            if (NativeFunction.Natives.HAS_ENTITY_CLEAR_LOS_TO_ENTITY<bool>(Game.LocalPlayer.Character, Suspects[0])
                                && Game.LocalPlayer.Character.Position.DistanceTo(Suspects[0]) < 45f + (playerRespondingInAdditon ? 20f : 0f)) {
                                LogVerboseDebug_withAiC("player has visual on suspect");
                                playerInvolved = true;
                                //someoneSpottedSuspect = true;
                                playerSpottedSuspect = true;
                                LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);                  //what should i do? the player cannot get into pursuit after reporting 
                                                                                                                //maybe due to the SetPursuitAsCalledIn(pursuit, false)
                                LSPDFR_Functions.SetPursuitAsCalledIn(pursuit, false);                          //problem: not beeing able to repot the pursuit. no pursuit radar?
                            }


                        //wenn player nach ai suspect sieht
                        if (someoneSpottedSuspect && !playerSpottedSuspect)
                        {
                            if (NativeFunction.Natives.HAS_ENTITY_CLEAR_LOS_TO_ENTITY<bool>(Game.LocalPlayer.Character, Suspects[0])
                                && Game.LocalPlayer.Character.Position.DistanceTo(Suspects[0]) < 70f)
                            {
                                LogVerboseDebug_withAiC("player has now visual on suspect too");
                                playerInvolved = true;
                                playerSpottedSuspect = true;
                                LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);
                                //LSPDFR_Functions.SetPursuitAsCalledIn(pursuit, true);               //the solution? no its not because the officer still wouldn't be able to call in the pursuit
                            }
                        }

                        //Is near enough to the Suspect
                        if (Game.LocalPlayer.Character.DistanceTo(Suspects[0]) < 15f && Game.LocalPlayer.Character.IsAlive)
                        {
                            LogVerboseDebug_withAiC("player is near enough to suspect");
                            suspectflees = true;
                            LSPDFR_Functions.SetPursuitDisableAIForPed(Suspects[0], false);
                        }

                        //--------------------------------------- Unit Officers ------------------------------------------
                        foreach (PatrolUnit u in Units)
                        {
                            foreach(Ped o in u.UnitOfficers)
                            {
                                if (o) 
                                {
                                    //Able to spot the Suspect
                                    if (!someoneSpottedSuspect)
                                        if (NativeFunction.Natives.HAS_ENTITY_CLEAR_LOS_TO_ENTITY<bool>(o, Suspects[0])
                                            && o.DistanceTo(Suspects[0]) < 50f)
                                        {
                                            LogVerboseDebug_withAiC("cop" + o + "has visual on suspect");
                                            someoneSpottedSuspect = true;
                                            if (playerSpottedSuspect) LSPDFR_Functions.SetPursuitAsCalledIn(pursuit, true);
                                            LSPDFR_Functions.SetPursuitCopsCanJoin(pursuit, true);
                                        }

                                    if (someoneSpottedSuspect) if (Suspects[0] ? !LSPDFR_Functions.IsPedInPursuit(o) : false) LSPDFR_Functions.AddCopToPursuit(pursuit, o);

                                    //Arrived at the Scene still moving
                                    if (o.IsAlive && o.IsInVehicle(u.PoliceVehicle, false)
                                    && u.PoliceVehicle.Speed <= 4
                                    && u.PoliceVehicle.DistanceTo(Location) < arrivalDistanceThreshold + 40f
                                    && !someoneSpottedSuspect)
                                    {
                                        if (u.PoliceVehicle.Driver == o) u.PoliceVehicle.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
                                    }

                                    //Arrived at the Scene - Get out
                                    if (o.IsAlive && o.IsInVehicle(u.PoliceVehicle, false)
                                    && u.PoliceVehicle.Speed == 0
                                    && u.PoliceVehicle.DistanceTo(Location) < arrivalDistanceThreshold + 40f
                                    && !someoneSpottedSuspect)
                                    {
                                        o.Tasks.LeaveVehicle(LeaveVehicleFlags.LeaveDoorOpen);
                                    }

                                    //Is near enough to the Suspect
                                    if (o.IsAlive && o.DistanceTo(Suspects[0]) < 15f)
                                    {
                                        suspectflees = true;
                                        LSPDFR_Functions.SetPursuitDisableAIForPed(Suspects[0], false);
                                    }
                                }

                            }

                        }

                        #endregion


                        #region Suspect Tasks
                        if (!Suspects[0].IsAlive) suspectalive = false;
                        
                        if (suspectalive && !suspectflees) {
                            //NativeFunction.Natives.TASK_COMBAT_HATED_TARGETS_IN_AREA(Suspects[0], Location, arrivalDistanceThreshold + 40f, null);
                            var ped = getClosestOfficer();
                            if (ped != null) Suspects[0].Tasks.FightAgainst(ped);
                        }
                        #endregion
                    }

                    GameFiber.Sleep(100);
                }

                //_------------------------------------------------------------------------------ Wenn niemand mehr schießt
                if (false) ;


                //leave when dead for now
                foreach (var unit in Units)
                {
                    GameFiber.StartNew(delegate {
                        var tmpUnit = unit;
                        try
                        {
                            EnterAndDismiss(tmpUnit);
                            foreach (var blip in tmpUnit.PoliceVehicle.GetAttachedBlips()) if (blip) blip.Delete();
                            foreach (var ofc in tmpUnit.UnitOfficers) {
                                if (ofc) 
                                    foreach (var blip in ofc.GetAttachedBlips())
                                    {
                                        if (blip.IsValid()) { blip.Delete(); }
                                    }
                            }

                            tmpUnit.PoliceVehicle.TopSpeed = 25f;
                        }
                        catch { }
                    });
                    GameFiber.Yield();
                }

                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AiCallout object: At Process(): " + e);
                return false;
            }
        }

        private Ped getClosestOfficer()
        {
            var list = Suspects[0].GetNearbyPeds(12);
            foreach (var ped in list)
            {
                if (ped)
                {
                    if (LSPDFR_Functions.IsPedACop(ped)
                        || ped == Game.LocalPlayer.Character)
                    {
                        return ped;
                    }
                }
            }
            return null;
        }

        private LSPD_First_Response.Mod.API.LHandle InitiatePursuit()
        {
            var firstShotDurration = 3000;
            var pursuit = LSPDFR_Functions.CreatePursuit();
            foreach (var suspect in Suspects)
            {
                if (new Random().Next(2) == 0) { suspect.Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_MICROSMG"), 200, true); } else { suspect.Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_PISTOL"), 200, true); }
                LSPDFR_Functions.AddPedToPursuit(pursuit, suspect);

                LSPDFR_Functions.SetPursuitDisableAIForPed(suspect, true);
                suspect.Tasks.FireWeaponAt(Units[0].PoliceVehicle, firstShotDurration + 2000, FiringPattern.BurstFire);
            }

            foreach (var cop in Units[0].UnitOfficers)
            {
                LSPDFR_Functions.AddCopToPursuit(pursuit, cop);
            }

            GameFiber.Sleep(firstShotDurration);
            return pursuit;
        }

        public override bool End()
        {
            //Code for finishing the the scene. return true when Succesfull.
            //Example idea: Cops getting back into their vehicle. drive away dismiss the rest. after 90 secconds delete if possible entitys that have not moved away.
            try
            {
                while (Game.LocalPlayer.Character.DistanceTo(Location) < 50f) GameFiber.Sleep(500);
                if (Suspects[0]) Suspects[0].Delete();
                return true;
            }
            catch (System.Threading.ThreadAbortException) { return false; }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At End(): " + e);
                return false;
            }
        }

        private void CleanUpEntitys()
        {
            try
            {
                foreach(var sus in Suspects)
                {
                    if (sus) sus.Delete();
                }
                foreach (var unit in Units)
                {
                    foreach(var cop in unit.UnitOfficers)
                    {
                        if (cop) cop.Delete();
                    }
                }
                foreach (var suscar in SuspectsVehicles)
                {
                    if (suscar) suscar.Delete();
                }
            } catch (Exception e)
            {
                LogTrivial_withAiC($"ERROR: in CleanUpEntitys: {e}");
            }
        }
    }
}