using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rage;
using LSPDFR_Functions = LSPD_First_Response.Mod.API.Functions;
using AmbientAICallouts.API;
using LSPD_First_Response.Mod.API;
using System.Xml;

namespace OutstandingWarrant
{
    public class OutstandingWarrant : AiCallout
    {
        private static List<Model> lostModels = new List<Model>() { "G_M_Y_LOST_01", "G_M_Y_LOST_02", "G_M_Y_LOST_03" };
        private static List<Model> ballasModels = new List<Model>() { "G_M_Y_BALLAEAST_01", "G_M_Y_BALLAORIG_01", "G_M_Y_BALLASOUT_01" };
        private static List<Model> vagosModels = new List<Model>() { "G_M_Y_SALVABOSS_01", "G_M_Y_SALVAGOON_01", "G_M_Y_SALVAGOON_02", "G_M_Y_SALVAGOON_03" };

        private static List<AmbientAICallouts.API.Functions.PositionWithHeading> spawnPoints = new List<AmbientAICallouts.API.Functions.PositionWithHeading>() {
            new AmbientAICallouts.API.Functions.PositionWithHeading(new Vector3(-2000f, -2099f, 0f), 0f),                           //Error Position                             //Error Position
            new AmbientAICallouts.API.Functions.PositionWithHeading(new Vector3(318.3842f, -1027.835f, 29.21791f), 176.263f),       //Pillbox
            new AmbientAICallouts.API.Functions.PositionWithHeading(new Vector3(-273.2328f, 6216.743f, 31.49138f), 130.2843f),      //BlaineCounty noth the lake
            new AmbientAICallouts.API.Functions.PositionWithHeading(new Vector3(1731.64f, 3707.406f, 34.1066f), 20.75879f),         //BlaineCountey south of the lake
            new AmbientAICallouts.API.Functions.PositionWithHeading(new Vector3(1695.706f, 3763.331f, 34.55957f), 315.3837f),       //BlaineCountey2 south of the lake
            new AmbientAICallouts.API.Functions.PositionWithHeading(new Vector3(1178.465f, 2646.26f, 37.79255f), 359.8123f),        //sherrifscounty 1
            new AmbientAICallouts.API.Functions.PositionWithHeading(new Vector3(269.164f, 197.4158f, 104.7828f), 161.2535f),        //Hollywood street
            new AmbientAICallouts.API.Functions.PositionWithHeading(new Vector3(11.2466f, -1953.999f, 20.75132f), 41.50063f),       //Grovestreet
            new AmbientAICallouts.API.Functions.PositionWithHeading(new Vector3(213.4791f, -1555.299f, 29.29156f), 207.7418f),      //Davis Ave
            new AmbientAICallouts.API.Functions.PositionWithHeading(new Vector3(-289.6428f, -1090.712f, 23.86914f), 242.8085f),     //alita street
        };
        
        public override bool Setup()
        {
            try
            {
                SceneInfo = "Outstanding Warrant";

                var nextLocation = spawnPoints[0];
                foreach (var checkLocation in spawnPoints)
                {
                    if (checkLocation.Position.DistanceTo(Game.LocalPlayer.Character.Position) < nextLocation.Position.DistanceTo(Game.LocalPlayer.Character.Position))
                        if (checkLocation.Position.DistanceTo(Game.LocalPlayer.Character.Position) < AmbientAICallouts.API.Functions.maximumAiCalloutDistance 
                         && checkLocation.Position.DistanceTo(Game.LocalPlayer.Character.Position) > AmbientAICallouts.API.Functions.minimumAiCalloutDistance)
                            nextLocation = checkLocation;
                }

                if (nextLocation.Position == spawnPoints[0].Position) { LogTrivial_withAiC("ERROR: Aborting AiCallout because of missing spawnpoint matching the callout distance"); return false; }

                location = nextLocation.Position;
                calloutDetailsString = "OUTSTANDING_WARRANT";
               arrivalDistanceThreshold = 15f;

                SetupSuspects(1);
                //Suspects[0].Model = new Model("");   //find a better moddel. it would be great if they would be just mean from gangs
                Suspects[0].Position = nextLocation.Position;
                Suspects[0].Heading = nextLocation.Heading;
                var Persona = LSPDFR_Functions.GetPersonaForPed(Suspects[0]);
                Persona.Wanted = true;
                Suspects[0].Tasks.PlayAnimation(new AnimationDictionary("mp_cop_tutdealer_leaning@base"), "base", 1f, AnimationFlags.Loop);

                if (Suspects[0])
                {
                    return true;
                }
                else { 
                    return false; 
                }
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
                if (!IsUnitInTime(150f, 130))  //if vehicle is never reaching its location                                                          //loger so that player can react
                {
                    Disregard();
                } else 
                {
                    Unit.TopSpeed = 16f; //mache einen krassen aproach
                    GameFiber.SleepUntil(
                        () => Unit.Position.DistanceTo(location) < 33f
                        || Game.LocalPlayer.Character.Position.DistanceTo(Suspects[0].Position) < 33f
                        , 0);   //bin ich oder die Unit angekommen?

                    GameFiber.StartNew(delegate {
                        GameFiber.Sleep(4000);
                        Suspects[0].Tasks.PlayAnimation(new AnimationDictionary("mp_cop_tutdealer_leaning@exit_aggressive"), "aggressive_exit", 1f, AnimationFlags.None);
                    });

                    LogTrivialDebug_withAiC($"DEBUG: Starting Animation maker Fiber");
                    bool senarioTaskAsigned = true;
                    GameFiber.StartNew(delegate
                    {
                        try
                        {
                            List<string> idleAnims = new List<string>() { "idle_a", "idle_b", "idle_c" };
                            while (Suspects[0])
                            {                                                                                   //sollange call läuft //Workaround
                                while (Suspects[0].IsAlive && !LSPDFR_Functions.IsPedArrested(Suspects[0]))
                                {
                                    while (!senarioTaskAsigned)
                                    {
                                        if (Suspects[0].Tasks.CurrentTaskStatus != Rage.TaskStatus.InProgress && Suspects[0].Tasks.CurrentTaskStatus != Rage.TaskStatus.Preparing) 
                                            if (Suspects[0] && !LSPDFR_Functions.IsPedArrested(Suspects[0]) && !LSPDFR_Functions.IsPedBeingCuffed(Suspects[0]) && !LSPDFR_Functions.IsPedBeingFrisked(Suspects[0]) && !LSPDFR_Functions.IsPedBeingGrabbed(Suspects[0]) && !LSPDFR_Functions.IsPedInPursuit(Suspects[0])) 
                                                Suspects[0].Tasks.PlayAnimation(new AnimationDictionary("oddjobs@towingangryidle_a"), idleAnims[new Random().Next(1, idleAnims.Count)], 1f, AnimationFlags.None); //} catch (Exception e) { LogTrivialDebug_withAiC()($"[AmbientAICallouts] [Fiber {fiberNumber}]  WARNING: Animation failed: " + e); }
                                        GameFiber.Sleep(8500);
                                    }
                                    GameFiber.Sleep(2000);
                                }
                                GameFiber.Sleep(2000);
                            }
                        }
                        catch (Exception e) { LogTrivialDebug_withAiC($"ERROR: in Animation maker Fiber: {e}"); }
                    }, $"[AmbientAICallouts] [AiCallout] Shooting - Animation maker Fiber");

                    GameFiber.SleepUntil(() => Unit.Position.DistanceTo(location) < arrivalDistanceThreshold + 10f, 30000);
                    UnitOfficers[0].PlayAmbientSpeech("S_M_Y_COP_01_WHITE_FULL_02", "COP_ARRIVAL_ANNOUNCE_MEGAPHONE", 0, SpeechModifier.Force);
                    GameFiber.SleepUntil(
                        () => Unit.Driver.Tasks.CurrentTaskStatus == Rage.TaskStatus.NoTask 
                        || Unit.Position.DistanceTo(location) < arrivalDistanceThreshold + 2f 
                        && Unit.Speed <= 1
                        , 30000);
                    OfficersLeaveVehicle(true);

                    LogTrivialDebug_withAiC($"DEBUG: Aproach and Aim");
                    int task = 0;                                         //0 = aproaching & aiming, 1 = aiming, 2 = shooting, 3 = lasttimeaproach, 4 = aimOnce
                    LogVerboseDebug_withAiC($"DEBUG: get Task 0 -------- 0 = aproaching & aiming, 1 = aiming, 2 = shooting, 3 = lasttimeaproach, 4 = aimOnce");


                    #region AI Hanlder
                    foreach (var officer in UnitOfficers) 
                    {
                        officer.Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_PISTOL"), 30, true);
                        GameFiber.StartNew(delegate
                        {
                            var thisOfficer = officer;
                            float innerRange = 10f;
                            while (task == 0)
                            {
                                if (officer)
                                {
                                    if (thisOfficer.Tasks.CurrentTaskStatus != Rage.TaskStatus.InProgress && thisOfficer.Tasks.CurrentTaskStatus != Rage.TaskStatus.Preparing)
                                    {
                                        try {
                                            if (thisOfficer.Position.DistanceTo(Suspects[0].Position) > innerRange + 2f)
                                            {
                                                thisOfficer.Tasks.GoToWhileAiming(Suspects[0].Position, Suspects[0].Position, innerRange, 2f, false, FiringPattern.SingleShot);
                                            } else
                                            {
                                                thisOfficer.Tasks.AimWeaponAt(Suspects[0].Position, 8000);
                                            }
                                        } catch { }
                                    }
                                }
                                GameFiber.Yield();
                            }
                            thisOfficer.Tasks.Clear();
                            while (task == 1)
                            {
                                if (officer)
                                {
                                    if (thisOfficer.Tasks.CurrentTaskStatus != Rage.TaskStatus.InProgress && thisOfficer.Tasks.CurrentTaskStatus != Rage.TaskStatus.Preparing && (Suspects[0] ? !Suspects[0].IsDead : false))
                                        try { thisOfficer.Tasks.AimWeaponAt(Suspects[0].Position, 8000); } catch { }
                                }
                                GameFiber.Yield();
                            }
                            thisOfficer.Tasks.Clear();
                            while (task == 2)
                            {
                                if (officer)
                                {
                                    if (thisOfficer.Tasks.CurrentTaskStatus != Rage.TaskStatus.InProgress && thisOfficer.Tasks.CurrentTaskStatus != Rage.TaskStatus.Preparing && (Suspects[0] ? !LSPDFR_Functions.IsPedGettingArrested(Suspects[0]) && !LSPDFR_Functions.IsPedArrested(Suspects[0]): false) )
                                    {
                                        try {
                                            LSPDFR_Functions.SetCopIgnoreAmbientCombatControl(officer, true);
                                            thisOfficer.Tasks.FightAgainst(Suspects[0]); 
                                        } catch { }
                                    }
                                }
                                GameFiber.Yield();
                            }
                            thisOfficer.Tasks.Clear();
                            while (task == 3)
                            {
                                if (officer)
                                {
                                    if (thisOfficer.Tasks.CurrentTaskStatus != Rage.TaskStatus.InProgress && thisOfficer.Tasks.CurrentTaskStatus != Rage.TaskStatus.Preparing)
                                    {
                                        try
                                        {
                                            if (thisOfficer.Position.DistanceTo(Suspects[0].Position) > 3f + 2f)
                                            {
                                                thisOfficer.Tasks.GoToWhileAiming(Suspects[0].Position, Suspects[0].Position, 3f, 2f, false, FiringPattern.SingleShot).WaitForCompletion();
                                                officer.Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_UNARMED"), 1, true);
                                            }
                                        }
                                        catch { }
                                    }
                                }
                                GameFiber.Yield();
                            }
                            thisOfficer.Tasks.Clear();
                            bool once = false;
                            while (task == 5 && !once)
                            {
                                if (officer)
                                {
                                    try { thisOfficer.Tasks.AimWeaponAt(Suspects[0].Position, 80000); } catch { }
                                }
                                once = true;
                            }

                        });
                        LSPDFR_Functions.SetCopIgnoreAmbientCombatControl(officer, false);
                        GameFiber.Sleep(200);
                    }
                    #endregion


                    GameFiber.Sleep(8000);
                    //task = 1;
                    //LogVerboseDebug_withAiC($"DEBUG: get Task 1");


                    if (UnitOfficers[1]) { UnitOfficers[1].PlayAmbientSpeech(null, "COP_ARRIVAL_ANNOUNCE", 0, SpeechModifier.Force); }
                    GameFiber.Sleep(12000);

                    senarioTaskAsigned = true;
                    Suspects[0].Tasks.Clear();
                    //shooter.Tasks.PlayAnimation(new AnimationDictionary(""), "", 1f, AnimationFlags.None);
                    GameFiber.Sleep(300);

                    Suspects[0].Inventory.GiveNewWeapon(new WeaponAsset("WEAPON_PISTOL"), 30, true);
                    GameFiber.Sleep(500);
                    UnitOfficers[0].PlayAmbientSpeech("S_M_Y_COP_01_WHITE_FULL_01", "COP_SEES_WEAPON", 0, SpeechModifier.Force);
                    GameFiber.Sleep(1200);
                    if (UnitOfficers[1]) { UnitOfficers[1].PlayAmbientSpeech("S_M_Y_COP_01_WHITE_FULL_02", "DRAW_GUN", 0, SpeechModifier.Force); }
                    else { UnitOfficers[0].PlayAmbientSpeech(new AnimationDictionary("S_M_Y_COP_01_WHITE_FULL_01"), "DRAW_GUN", 0, SpeechModifier.Force); }
                    GameFiber.Sleep(2000);


                    if (IsAiTakingCare()) //checkforSelfhandle
                    {
                        
                        LogTrivial_withAiC($"INFO: chose selfhandle path");
                        Suspects[0].Tasks.AimWeaponAt(UnitOfficers[0], 30000);
                        GameFiber.Sleep(900);
                        if (UnitOfficers[1]) { UnitOfficers[1].PlayAmbientSpeech("A_M_M_GENERICMALE_01_WHITE_MINI_02", "DROP_THE_WEAPON", 0, SpeechModifier.Force); GameFiber.Sleep(1000); }
                        UnitOfficers[0].PlayAmbientSpeech(new AnimationDictionary("A_M_M_GENERICMALE_01_WHITE_MINI_01"), "DROP_THE_WEAPON", 0, SpeechModifier.Force);
                        GameFiber.Sleep(900);
                        if (UnitOfficers[1]) { UnitOfficers[1].PlayAmbientSpeech("A_M_M_GENERICMALE_01_WHITE_MINI_02", "DROP_THE_WEAPON", 0, SpeechModifier.Force); GameFiber.Sleep(1000); }
                        UnitOfficers[0].PlayAmbientSpeech(new AnimationDictionary("A_M_M_GENERICMALE_01_WHITE_MINI_01"), "DROP_THE_WEAPON", 0, SpeechModifier.Force);
                        GameFiber.Sleep(900);
                        if (UnitOfficers[1]) { UnitOfficers[1].PlayAmbientSpeech("A_M_M_GENERICMALE_01_WHITE_MINI_02", "DROP_THE_WEAPON", 0, SpeechModifier.Force); GameFiber.Sleep(1000); }
                        UnitOfficers[0].PlayAmbientSpeech(new AnimationDictionary("A_M_M_GENERICMALE_01_WHITE_MINI_01"), "DROP_THE_WEAPON", 0, SpeechModifier.Force);
                        GameFiber.Sleep(2000);

                        Suspects[0].Tasks.FireWeaponAt(UnitOfficers[0], 20000, FiringPattern.BurstFirePistol);
                        Suspects[0].RelationshipGroup = new RelationshipGroup("PRISONER");
                        Suspects[0].RelationshipGroup.SetRelationshipWith("COP", Relationship.Hate);
                        task = 2;
                        LogVerboseDebug_withAiC($"DEBUG: get Task 2");

                        GameFiber.SleepUntil(() => Suspects[0].IsDead, 30000);
                        if (Suspects[0].IsDead)
                        {
                            task = 3;
                            LogVerboseDebug_withAiC($"DEBUG: get Task 3");
                            GameFiber.Sleep(6000);
                            //var RadioOfficerIndex = MathHelper.GetRandomInteger(0, UnitOfficers.Count);          //Radio Animation. Future
                            //UnitOfficers[RadioOfficerIndex].Tasks.PlayAnimation()

                            GameFiber.Sleep(5000);
                            if (!LSPDFR_Functions.IsPedArrested(Suspects[0]) && !LSPDFR_Functions.IsPedGettingArrested(Suspects[0]) ) { Suspects[0].Delete(); }
                            GameFiber.Sleep(5000);
                            EnterAndDismiss();
                        }
                        else
                        {
                            var Pursuit = LSPDFR_Functions.CreatePursuit();
                            LSPDFR_Functions.AddPedToPursuit(Pursuit, Suspects[0]);
                            while (LSPDFR_Functions.IsPursuitStillRunning(Pursuit)) { GameFiber.Sleep(1000); }
                            //ISSUE: Officers & Peds get Dismissed before the Arrest is fullfilled.
                        }
                    }
                    else //Callout Suspects() are getting agressive 
                    {
                        LogTrivial_withAiC($"INFO: choosed callout path");
                        task = 5;
                        LogVerboseDebug_withAiC($"DEBUG: get Task 5");

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
                            //default:
                                //    UnitCallsForBackup("OfficerRequiringAssistance");
                                //    break;
                        }
                        GameFiber.Sleep(15000);
                        while (LSPDFR_Functions.IsCalloutRunning()) { GameFiber.Sleep(11000); } //OLD: while (!OfficerRequiringAssistance.finished) { GameFiber.Sleep(11000); }
                    }
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
            try
            {
                ///doStuff
                return true;
            }
            catch (Exception e)
            {
                LogTrivial_withAiC("ERROR: in AICallout object: At End(): " + e);
                return false;
            }
        }
    }
}

