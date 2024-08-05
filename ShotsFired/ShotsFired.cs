using System;
using System.Data.Common;
using System.ComponentModel;
using Rage;
using Rage.Native;
using AmbientAICallouts;
using AmbientAICallouts.API;
using LSPDFR_Functions = LSPD_First_Response.Mod.API.Functions;
using System.Linq;
using System.Windows.Forms;
using RAGENativeUI;
using System.Collections.Generic;

namespace ShotsFired
{
    public class ShotsFired : AiCallout
    {
        public override int UnitsNeeded => 3;

        Random randomizer = new Random();
        LSPD_First_Response.Mod.API.LHandle pursuit;
        RAGENativeUI.Elements.TimerBarPool timerBarPool = new RAGENativeUI.Elements.TimerBarPool();
        RAGENativeUI.Elements.BarTimerBar progressbar = new RAGENativeUI.Elements.BarTimerBar("Reporting PC503");
        
        bool calledInByCrimeReport = false;

        Keys ACTION_CRIME_REPORT_key;
        Keys ACTION_CRIME_REPORT_modifier_key;
        ControllerButtons ACTION_CRIME_REPORT_button;
        ControllerButtons ACTION_CRIME_REPORT_modifier_button;    

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

                CopyLspdlfr_Action_Crime_Report_Inputs();    //ToDo: check names from lspdfr ini or lspdfr docs

                #region Spawnpoint searching
                Vector3 roadside = new Vector3();
                bool posFound = false;
                int trys = 0;
                while (!posFound && trys < 30)
                {
                    roadside = World.GetNextPositionOnStreet(Game.LocalPlayer.Character.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                    //Vector3 irrelevant;
                    //float heading = 0f;       //vieleicht guckt der MVA dann in fahrtrichtung der unit


                    //NativeFunction.Natives.xA0F8A7517A273C05<bool>(roadside.X, roadside.Y, roadside.Z, heading, out roadside); //_GET_ROAD_SIDE_POINT_WITH_HEADING
                    //NativeFunction.Natives.xFF071FB798B803B0<bool>(roadside.X, roadside.Y, roadside.Z, out irrelevant, out heading, 0, 3.0f, 0f); //GET_CLOSEST_VEHICLE_NODE_WITH_HEADING //Find Side of the road.

                    NativeFunction.Natives.xB61C8E878A4199CA<bool>(roadside, true, out roadside, 16); //GET_SAFE_COORD_FOR_PED
                    Location = roadside;


                    if (Functions.IsLocationAcceptedBySystem(Location))
                        posFound = true;

                    trys++;
                    if (trys >= 30) { LogTrivial_withAiC("ERROR: in AICallout object: At Setup(): unable to find safe coords for this event"); return false;}
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
                            if (!IsUnitInTime(tmpUnit, arrivalDistanceThreshold + 40f, randomizer.Next(125,140)))  //if vehicle is never reaching its location
                            { 
                                if (Suspects[0])
                                    if (Suspects[0].IsAlive) {

                                        if (tmpUnit.PoliceVehicle) { 
                                            Game.DisplayNotification($"~b~{tmpUnit.RadioVoice.CallSignString}~w~: {(tmpUnit.UnitOfficers.Count <= 1 ? "I'm stuck in traffic. \nShow me canceling." : "We are out. We're stuck in traffic.")}");
                                        }

                                        foreach(var unitOfc in tmpUnit.UnitOfficers) { if (unitOfc) unitOfc.Delete(); }
                                        tmpUnit.PoliceVehicle.Delete();
                                        Units.Remove(tmpUnit);
                                    }
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
                bool aiSpottedSuspect = false;
                bool playerSpottedSuspect = false;
                bool suspectflees = false;
                int suspectInvalidCounter = 0;
               
                progressbar.ForegroundColor = System.Drawing.Color.Red;

                int tickcounter = 0;    //to count sleep processes to reach a specifc time.
                bool acr_active = false;

                pursuit = LSPDFR_Functions.CreatePursuit();
                LSPDFR_Functions.SetPursuitAsCalledIn(pursuit, false);
                LSPDFR_Functions.AddPedToPursuit(pursuit, Suspects[0]);
                LSPDFR_Functions.SetPursuitDisableAIForPed(Suspects[0], true);
                LSPD_First_Response.Mod.API.Functions disablecrimereportTempVar = new LSPD_First_Response.Mod.API.Functions();  //Awaiting change through LMS!
                disablecrimereportTempVar.SetPedDisableCrimeEvents(Suspects[0], true);                                          //Awaiting change through LMS!
                var attributes = LSPDFR_Functions.GetPedPursuitAttributes(Suspects[0]);
                attributes.AverageFightTime = 1;
                if (new Random().Next(2) == 0) { Suspects[0].Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_MICROSMG"), 200, true); } else { Suspects[0].Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_PISTOL"), 200, true); }

                GameFiber.StartNew(delegate { try { 
                        GameFiber.SleepUntil(() => acr_active, 120000);
                        while (suspectalive && Suspects[0] && !aiSpottedSuspect) {                      //when recreating a suspect this would lead to kill the RNUI progressbar fiber
                            while (acr_active && !aiSpottedSuspect && progressbar.Percentage > 0f)
                            {
                                timerBarPool.Draw();
                                GameFiber.Yield();
                            }
                            GameFiber.Yield();
                        }

                } catch { } }, "[AAIC] - Shotsfired - RNUI Progressbar");
                
                while (suspectalive)
                {
                    if (!Suspects[0])//!Functions.EntityValidityChecks(MO, false))
                    {
                        if (suspectInvalidCounter % 30 == 0) LogTrivialDebug_withAiC("WARNING: Suspect is currently Invalid. Cannot process Script.");

                        if (suspectInvalidCounter > 100)    //sleep ist 100 ms also mal 100 = 10 sekunden.
                        {
                            //ToDo: Recreate the Suspect to keep the callout going.
                            LogTrivial_withAiC("ERROR: in AICallout object: At Process(): Suspect entity was too long invalid while having them on persistent. Aborting Callout");
                            CleanUpEntitys();
                            return false;
                        }

                        suspectInvalidCounter++;
                    }
                    else
                    {
                        suspectInvalidCounter = 0;
                        //-------------------------------------- Player -----------------------------------------------------
                        #region Officer Tasks

                        //Player Spottet Suspect - first time
                        if (!aiSpottedSuspect && !playerSpottedSuspect) {
                            if (CloseEnoughForReportAt(Suspects[0]))
                            {
                                LogVerboseDebug_withAiC("player has visual on suspect");
                                playerSpottedSuspect = true;
                                //LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);
                                //LSPDFR_Functions.SetPursuitAsCalledIn(pursuit, false);          //Test
                                acr_active = true;
                                timerBarPool.Add(progressbar);
                            }
                        }
                        //Player lost Suspect while unreported
                        else if (!aiSpottedSuspect && playerSpottedSuspect && acr_active) { 
                            if (!CloseEnoughForReportAt(Suspects[0])) { acr_active = false; }
                        }
                        //Player Spottet Suspect - again
                        else if (!aiSpottedSuspect && playerSpottedSuspect && !acr_active) { 
                            if (CloseEnoughForReportAt(Suspects[0])) { acr_active = true; }
                        }

                        //Plyer reporting or able to report
                        if (!aiSpottedSuspect && playerSpottedSuspect && acr_active)
                        {
                            if (tickcounter % 100 == 0) Game.DisplayHelp($"If you see the suspect, \nreport its position by holding ~{ACTION_CRIME_REPORT_key.GetInstructionalId()}~", 10000); //10 sekunden warten //ToDo: Show current input device as help info (controler or keys)
                            if (ACTION_CRIME_REPORT_pressed())
                            {
                                progressbar.Percentage += 0.05f;  //2 sec = 20 ticks. 100 / 20 = 5; Proof: 5f * 20 ticks(oder 2 sek) = 100%
                            }
                            else if (progressbar.Percentage > 0f)
                            {
                                progressbar.Percentage -= 0.10f; //1 sec = 10 ticks. 100 / 10 = 10; Proof: 10f * 10 ticks(oder 1 sek) = 100%
                            }
                            
                            
                            if (progressbar.Percentage >= 1f) { acr_active = false; aiSpottedSuspect = true; timerBarPool.Remove(progressbar); calledInByCrimeReport = true;  callInPursuit(); }
                        }

                        //Wenn der Player NACH den AiUnits den Suspect sieht.
                        if (aiSpottedSuspect && !playerSpottedSuspect)
                        {
                            if (NativeFunction.Natives.xFCDFF7B72D23A1AC<bool>(Game.LocalPlayer.Character, Suspects[0]) //HAS_ENTITY_CLEAR_LOS_TO_ENTITY
                                && Game.LocalPlayer.Character.Position.DistanceTo(Suspects[0]) <= 40f + (playerRespondingInAdditon ? 30f : 0f))
                            {
                                LogVerboseDebug_withAiC("player has now visual on suspect too");
                                playerSpottedSuspect = true;
                                LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);
                                //possibly placing pursuit in progress msg
                            }
                        }

                        //Is near enough to the Suspect to trigger running mode
                        if (Game.LocalPlayer.Character.DistanceTo(Suspects[0]) < 15f && Game.LocalPlayer.Character.IsAlive)
                        {
                            LogVerboseDebug_withAiC("suspect starts running");
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
                                    if (!aiSpottedSuspect)
                                        if (NativeFunction.Natives.xFCDFF7B72D23A1AC<bool>(o, Suspects[0]) //HAS_ENTITY_CLEAR_LOS_TO_ENTITY
                                            && o.DistanceTo(Suspects[0]) < 50f)
                                        {
                                            LogVerboseDebug_withAiC("Officer " + o + " has visual on suspect");
                                            aiSpottedSuspect = true;
                                            if (!LSPDFR_Functions.IsPursuitCalledIn(pursuit))
                                                callInPursuit();
                                        }

                                    if (aiSpottedSuspect) if (Suspects[0] ? !LSPDFR_Functions.IsPedInPursuit(o) : false) LSPDFR_Functions.AddCopToPursuit(pursuit, o);

                                    //Arrived at the Scene still moving
                                    if (o.IsAlive && o.IsInVehicle(u.PoliceVehicle, false)
                                    && u.PoliceVehicle.Speed <= 4
                                    && u.PoliceVehicle.DistanceTo(Location) < arrivalDistanceThreshold + 40f
                                    && !aiSpottedSuspect)
                                    {
                                        if (u.PoliceVehicle.Driver == o) u.PoliceVehicle.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
                                    }

                                    //Arrived at the Scene - Get out
                                    if (o.IsAlive && o.IsInVehicle(u.PoliceVehicle, false)
                                    && u.PoliceVehicle.Speed == 0
                                    && u.PoliceVehicle.DistanceTo(Location) < arrivalDistanceThreshold + 40f
                                    && !aiSpottedSuspect)
                                    {
                                        o.Tasks.LeaveVehicle(LeaveVehicleFlags.LeaveDoorOpen);
                                    }

                                    //Is near enough to the Suspect
                                    if (o.IsAlive && o.DistanceTo(Suspects[0]) < 15f)
                                    {
                                        suspectflees = true;
                                        LSPDFR_Functions.SetPursuitDisableAIForPed(Suspects[0], false);
                                    }

                                    //Arrived at the Scene - Get out
                                    if (o.IsAlive && o.IsInVehicle(u.PoliceVehicle, false)
                                    && u.PoliceVehicle.Speed <= 0.2
                                    && u.PoliceVehicle.DistanceTo(Location) < arrivalDistanceThreshold + 40f
                                    && !aiSpottedSuspect)
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
                        if (!Suspects[0].IsAlive) 
                            suspectalive = false;
                        
                        if (suspectalive && !suspectflees) {
                            //NativeFunction.Natives.TASK_COMBAT_HATED_TARGETS_IN_AREA(Suspects[0], Location, arrivalDistanceThreshold + 40f, null);
                            var ped = getClosestOfficer();
                            if (ped != null) Suspects[0].Tasks.FightAgainst(ped);
                        }
                        #endregion
                    }
                    tickcounter++;
                    GameFiber.Sleep(100);
                }

                //------------------------------------------------------------------------------ Wenn niemand mehr schießt
                //if (false) ;


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
                LogTrivial_withAiC("ERROR: in AICallout object: At Process(): " + e);
                return false;
            }
        }

        private bool CloseEnoughForReportAt(Ped ped)
        {
            return
                    //has visual in a certain distance - kann ihn über eine bestimmte distance sehen
                    NativeFunction.Natives.xFCDFF7B72D23A1AC<bool>(Game.LocalPlayer.Character, Suspects[0])             //HAS_ENTITY_CLEAR_LOS_TO_ENTITY            
                    && Game.LocalPlayer.Character.Position.DistanceTo(Suspects[0]) <= 25f + (playerRespondingInAdditon ? 8f : 0f) + (Game.LocalPlayer.Character.IsInAnyVehicle(false) ? 8f : 0f)
                    
                    ||

                    //is without visual close enough to the suspect - ist ohne ihn zu sehen nah genug
                    Game.LocalPlayer.Character.Position.DistanceTo(Suspects[0]) <= 10f + (playerRespondingInAdditon ? 8f : 0f) + (Game.LocalPlayer.Character.IsInAnyVehicle(false) ? 8f : 0f);             
                                                    
        }

        private void callInPursuit()
        {            
            if (playerRespondingInAdditon || calledInByCrimeReport)
            {
                LSPDFR_Functions.SetPursuitAsCalledIn(pursuit, true);                   //ATTENTION: Keep this order to prevent losing the Pursuit Radar.
                LSPDFR_Functions.SetPursuitIsActiveForPlayer(pursuit, true);            //ATTENTION: Keep this order to prevent losing the Pursuit Radar.
                //Game.DisplayNotification(null, "CHAR_CALL911", "Dispatch", "pursuit initiated", $"Pursuit initiated at: {World.TimeOfDay}."); //ToDo: ersetzte felder.
                LSPDFR_Functions.PlayScannerAudioUsingPosition("ATTENTION_ALL_UNITS WE_HAVE A CRIME_SUSPECT_RESISTING_ARREST IN_OR_ON_POSITION", Location); //ToDo: Teste parameter der dispatch ruf function
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


        public override bool End()
        {
            //Code for finishing the the scene. return true when Succesfull.
            //Example idea: Cops getting back into their vehicle. drive away dismiss the rest. after 90 secconds delete if possible entitys that have not moved away.
            try
            {
                timerBarPool.Remove(progressbar);
                while (Game.LocalPlayer.Character.DistanceTo(Location) < 50f) GameFiber.Sleep(500);
                if (Suspects[0]) Suspects[0].IsPersistent = false;
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

        private bool ACTION_CRIME_REPORT_pressed()
        {
            return         //Either key is pressed               und                      modifier_key is null              or modifier_key is pressed
                (  Game.IsKeyDownRightNow(ACTION_CRIME_REPORT_key) && (ACTION_CRIME_REPORT_modifier_key == Keys.None ? true : Game.IsKeyDownRightNow(ACTION_CRIME_REPORT_modifier_key))
                || Game.IsControllerButtonDownRightNow(ACTION_CRIME_REPORT_button) && (ACTION_CRIME_REPORT_modifier_button == ControllerButtons.None ? true : Game.IsControllerButtonDownRightNow(ACTION_CRIME_REPORT_modifier_button))  );
        }

        private void CopyLspdlfr_Action_Crime_Report_Inputs()
        {
            InitializationFile LSPDFR_keys;
            int settingsCounter = 4;      //Indicates how many bools are needed for for the settings in order to work.
            List<bool> successfullySetup = new List<bool>(); for (int i = 0; i < settingsCounter; i++) { successfullySetup.Add(false); }
            bool settingsOK;

            try
            {
                LogTrivialDebug_withAiC("[initialization] DEBUG: Attempting to load lspdfr/keys.ini to get LSPDFR settings for ACTION CRIME REPORT input");
                LSPDFR_keys = new InitializationFile("lspdfr/keys.ini"); //ToDo: checked for correct file name. Verify this
                LSPDFR_keys.Create();
                LogTrivialDebug_withAiC("[initialization] DEBUG: lspdfr/keys.ini file loaded successfully");

                KeysConverter kc = new KeysConverter();
                List<Keys> keyList = new List<Keys>();

                LogTrivialDebug_withAiC("[initialization] DEBUG: Attempt to read settings");   
                try { ACTION_CRIME_REPORT_key = (Keys)kc.ConvertFromString(LSPDFR_keys.ReadString("", "CRIME_REPORT_Key", "B")); successfullySetup[0] = true; } catch { Game.LogTrivial($"[AmbientAICallouts] [initialization] WARNING: Couldn't read ACTION_CRIME_REPORT_key"); }
                try { ACTION_CRIME_REPORT_modifier_key = (Keys)kc.ConvertFromString(LSPDFR_keys.ReadString("", "CRIME_REPORT_ModifierKey", "None")); successfullySetup[1] = true; } catch { Game.LogTrivial($"[AmbientAICallouts] [initialization] WARNING: Couldn't read ACTION_CRIME_REPORT_modifier_key"); }
                try { ACTION_CRIME_REPORT_button = LSPDFR_keys.ReadEnum<ControllerButtons>("", "CRIME_REPORT_ControllerKey", ControllerButtons.RightThumb); successfullySetup[2] = true; } catch { Game.LogTrivial($"[AmbientAICallouts] [initialization] WARNING: Couldn't read ACTION_CRIME_REPORT_button"); }
                try { ACTION_CRIME_REPORT_modifier_button = LSPDFR_keys.ReadEnum<ControllerButtons>("", "CRIME_REPORT_ControllerModifierKey", ControllerButtons.None); successfullySetup[3] = true; } catch { Game.LogTrivial($"[AmbientAICallouts] [initialization] WARNING: Couldn't read ACTION_CRIME_REPORT_modifier_button"); }


                bool allOK = true;
                foreach (bool ok in successfullySetup) { if (ok == false) allOK = false; }

                if (allOK)
                {
                    Game.LogTrivial($"[AmbientAICallouts] [initialization] INFO: Successfully read settings");
                    settingsOK = true;
                }
                else
                {
                    Game.LogTrivial($"[AmbientAICallouts] [initialization] WARNING: Reading settings ERROR");
                    settingsOK = false;
                }

            }
            catch (System.Threading.ThreadAbortException) { settingsOK = false; }
            catch (Exception e)
            {
                LogTrivial_withAiC($"[initialization] WARNING: Fatal error reading LSPDFR keys from lspdfr/keys.ini, make sure your preferred settings are valid. Applying all default settings!");
                LogTrivial_withAiC($"[initialization] WARNING: error MSG{e}");
                settingsOK = false;

                ACTION_CRIME_REPORT_key = Keys.B;
                ACTION_CRIME_REPORT_modifier_key = Keys.None;
                ACTION_CRIME_REPORT_button = ControllerButtons.RightThumb;
                ACTION_CRIME_REPORT_modifier_button = ControllerButtons.None;

            }

            if (!settingsOK)
            {
                if (successfullySetup[0] == false) ACTION_CRIME_REPORT_key = Keys.B;
                if (successfullySetup[1] == false) ACTION_CRIME_REPORT_modifier_key = Keys.None;
                if (successfullySetup[2] == false) ACTION_CRIME_REPORT_button = ControllerButtons.RightThumb;
                if (successfullySetup[3] == false) ACTION_CRIME_REPORT_modifier_button = ControllerButtons.None;
            }

            LogTrivialDebug_withAiC("[initialization] DEBUG: Setting: Crime Report key = " + ACTION_CRIME_REPORT_key);
            LogTrivialDebug_withAiC("[initialization] DEBUG: Setting: Crime Report modifier key = " + ACTION_CRIME_REPORT_modifier_key);
            LogTrivialDebug_withAiC("[initialization] DEBUG: Setting: Crime Report button = " + ACTION_CRIME_REPORT_button);
            LogTrivialDebug_withAiC("[initialization] DEBUG: Setting: Crime Report modifier button = " + ACTION_CRIME_REPORT_modifier_button);
        }
    }
}