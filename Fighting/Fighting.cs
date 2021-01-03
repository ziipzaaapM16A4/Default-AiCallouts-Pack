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
        bool startLoosingHealth = false;
        public override bool Setup()
        {
            try
            {
                SceneInfo = "Fighting";
                bool posFound = false;
                int i = 0;
                while (!posFound && i < 20)
                {
                    location = World.GetNextPositionOnStreet(Unit.Position.Around(AmbientAICallouts.API.Functions.minimumAiCalloutDistance + 10f, AmbientAICallouts.API.Functions.maximumAiCalloutDistance - 10f));
                    if (Unit.Position.DistanceTo(location) > Functions.minimumAiCalloutDistance
                     && Unit.Position.DistanceTo(location) < Functions.maximumAiCalloutDistance)
                        posFound = true;
                    i++;
                }
                arrivalDistanceThreshold = 14f;
                calloutDetailsString = "CRIME_ASSAULT";
                SetupSuspects(2);

                Suspects[0].Tasks.FightAgainst(Suspects[1]);
                Suspects[1].Tasks.FightAgainst(Suspects[0]);

                GameFiber.StartNew(delegate { 
                    while (!startLoosingHealth)
                    {
                        foreach (var suspect in Suspects)
                        {
                            try { suspect.Health = 200; } catch { }
                        }
                        GameFiber.Sleep(1000);
                    }
                });

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
            try
            {
                if (!IsUnitInTime(100f, 130))  //if vehicle is never reaching its location
                {
                    Disregard();
                }
                else  //if vehicle is reaching its location
                {
                    GameFiber.WaitWhile(() => Unit.Position.DistanceTo(location) >= 40f, 0);
                    Unit.IsSirenSilent = true;
                    Unit.TopSpeed = 12f;
                    OfficerReportOnScene();

                    GameFiber.SleepUntil(() => location.DistanceTo(Unit.Position) < arrivalDistanceThreshold + 5f /* && Unit.Speed <= 1*/, 30000);
                    Unit.Driver.Tasks.PerformDrivingManeuver(VehicleManeuver.Wait);
                    GameFiber.SleepUntil(() => Unit.Speed <= 1, 5000);
                    OfficersLeaveVehicle(true);

                    startLoosingHealth = true;

                    LogTrivialDebug_withAiC($"DEBUG: Aim and aproach and Hands up");
                    foreach (var officer in UnitOfficers) { officer.Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_PISTOL"), 30, true); }

                    if (IsAiTakingCare())
                    {
                        LogTrivial_withAiC($"INFO: chose selfhandle path");
                        LogTrivialDebug_withAiC($"DEBUG: Aim and aproach and Hands up");
                        var taskGoWhileAiming0 = UnitOfficers[0].Tasks.GoToWhileAiming(Suspects[0], Suspects[0], 5f, 1f, false, FiringPattern.SingleShot);
                        if (UnitOfficers.Count > 1 && Suspects[1]) { UnitOfficers[1].Tasks.GoToWhileAiming(Suspects[1], Suspects[1], 5f, 1f, false, FiringPattern.SingleShot); }
                        else if (UnitOfficers.Count > 1) { UnitOfficers[1].Tasks.GoToWhileAiming(Suspects[0], Suspects[0], 5f, 1f, false, FiringPattern.SingleShot); }
                        GameFiber.WaitWhile(() => taskGoWhileAiming0.IsActive, 10000);
                        UnitOfficers[0].Tasks.AimWeaponAt(Suspects[0], 30000);
                        if (UnitOfficers.Count > 1) UnitOfficers[1].Tasks.AimWeaponAt(Suspects[1], 30000);
                        foreach (var suspect in Suspects) { if (suspect) suspect.Tasks.PutHandsUp(6000, UnitOfficers[0]); }
                        GameFiber.Sleep(9000);

                        LogTrivialDebug_withAiC($"DEBUG: Flee or Stay");
                        List<bool> underArrest = new List<bool>();
                        bool someoneStopped = false;


                        foreach (var suspect in Suspects)
                        {
                            if (suspect)
                            {
                                LSPDFR_Functions.SetPedResistanceChance(suspect, 0.35f);
                                if (LSPDFR_Functions.IsPedGettingArrested(suspect) || LSPDFR_Functions.IsPedArrested(suspect) || LSPDFR_Functions.IsPedStoppedByPlayer(suspect))
                                {
                                    someoneStopped = true;
                                    underArrest.Add(true);

                                }
                                else
                                {
                                    underArrest.Add(false);

                                }
                            }
                            else
                            {
                                underArrest.Add(false);
                            }
                        }

                        LogTrivialDebug_withAiC($"DEBUG: SomebodyStopped?");
                        if (someoneStopped)
                        {
                            bool finished = false;

                            List<bool> gettingArrested = new List<bool>();
                            while (!finished)
                            {
                                GameFiber.Sleep(500);

                                foreach (var suspect in Suspects)
                                {
                                    if (suspect)
                                    {
                                        if (LSPDFR_Functions.IsPedGettingArrested(suspect) || LSPDFR_Functions.IsPedStoppedByPlayer(suspect))
                                        {
                                            gettingArrested.Add(true);

                                        }
                                        else
                                        {
                                            gettingArrested.Add(false);

                                        }
                                    }
                                    else
                                    {
                                        gettingArrested.Add(false);
                                    }
                                }

                                if (gettingArrested.Any(ofc => ofc.Equals(false))) finished = true;
                            }

                            foreach (var officer in UnitOfficers) { if (officer) officer.Tasks.Clear(); }
                            EnterAndDismiss(false);
                        }
                        else
                        {
                            if (UnitOfficers.Count != 1) { if (UnitOfficers[1]) UnitOfficers[1].PlayAmbientSpeech(null, "CRIMINAL_WARNING", 0, SpeechModifier.Force); }
                            else if (UnitOfficers[0]) { UnitOfficers[0].PlayAmbientSpeech(null, "CRIMINAL_WARNING", 0, SpeechModifier.Force); }
                            foreach (var officer in UnitOfficers) { if (officer) officer.Tasks.Clear(); }
                            GameFiber.Sleep(4000);

                            underArrest = new List<bool>();
                            someoneStopped = false;
                            foreach (var suspect in Suspects)
                            {
                                if (suspect)
                                {
                                    if (LSPDFR_Functions.IsPedGettingArrested(suspect) || LSPDFR_Functions.IsPedArrested(suspect) || LSPDFR_Functions.IsPedStoppedByPlayer(suspect))
                                    {
                                        someoneStopped = true;
                                        underArrest.Add(true);
                                    }
                                    else
                                    {
                                        underArrest.Add(false);

                                    }
                                }
                                else
                                {
                                    underArrest.Add(false);
                                }
                            }


                            if (!someoneStopped) 
                                foreach (var suspect in Suspects) { 
                                    if (suspect) suspect.Tasks.Flee(UnitOfficers[0], 100f, 30000); 
                                    suspect.IsPersistent = false; 
                                }

                            GameFiber.Sleep(5100);
                            EnterAndDismiss(false);
                        }

                        LogTrivial_withAiC($"INFO: Call Finished");
                    }
                    else                                        //Callout
                    {
                        LogTrivial_withAiC($"INFO: choosed callout path");
                        LogTrivialDebug_withAiC($"DEBUG: Aim and aproach and Hands up");
                        //unitOfficers[0].Tasks.GoToWhileAiming(location, suspects[0], 10f, 1f, false, FiringPattern.SingleShot);
                        //if (UnitOfficers.Count > 1) unitOfficers[1].Tasks.GoToWhileAiming(location, suspects[1], 10f, 1f, false, FiringPattern.SingleShot);
                        UnitOfficers[0].Tasks.AimWeaponAt(Suspects[0], 18000);
                        if (UnitOfficers.Count > 1) UnitOfficers[1].Tasks.AimWeaponAt(Suspects[1], 18000);
                        foreach (var suspect in Suspects)
                        {
                            if (suspect && UnitOfficers[0])
                                suspect.Tasks.PutHandsUp(120000, UnitOfficers[0]);
                        }

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

                }
                return true;
            }
            catch (System.Threading.ThreadAbortException e)
            {
                LogTrivial_withAiC("WARNING: in AICallout object: At Process(): ThreadAbortException ---> LSPDFR has probably been Ended. Closing Fibers now..." + e);
                return false;
            }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At Process(): " + e);
                return false;
            }
        }
        public override bool End()
        {
            try
            {
                return true;
            }
            catch (Exception e)
            {
                LogTrivial_withAiC( "ERROR: in AICallout object: At End():" + e);
                return false;
            }
        }
    }
}