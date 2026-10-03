using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class CombatAnimatorGenerator
{
    [MenuItem("Tools/Combat/Generate Combat Animator Controller")]
    public static void GenerateCombatAnimator()
    {
        // Create folder structure
        string folderPath = "Assets/CombatAnimator";
        CreateFolderIfNeeded(folderPath);
        CreateFolderIfNeeded(folderPath + "/Animations");
        CreateFolderIfNeeded(folderPath + "/Masks");

        // Create the main animator controller
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(
            folderPath + "/CombatAnimatorController.controller");

        Debug.Log("Creating Combat Animator Controller...");

        // Add all parameters
        AddAnimatorParameters(controller);
        Debug.Log("Added animator parameters");

        // Create base locomotion layer
        CreateLocomotionLayer(controller);
        Debug.Log("Created locomotion layer");

        // Create upper body action layer
        CreateUpperBodyLayer(controller);
        Debug.Log("Created upper body layer");

        // Create bow aiming layer
        CreateBowAimingLayer(controller);
        Debug.Log("Created bow aiming layer");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[SUCCESS] Combat Animator Controller generated at: Assets/CombatAnimator/CombatAnimatorController.controller");
    }

    private static void CreateFolderIfNeeded(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            string parentPath = folderPath.Substring(0, folderPath.LastIndexOf("/"));
            string folderName = folderPath.Substring(folderPath.LastIndexOf("/") + 1);
            AssetDatabase.CreateFolder(parentPath, folderName);
        }
    }

    private static void AddAnimatorParameters(AnimatorController controller)
    {
        // Int parameters
        controller.AddParameter("CombatState", AnimatorControllerParameterType.Int);
        controller.AddParameter("WeaponType", AnimatorControllerParameterType.Int);

        // Float parameters
        controller.AddParameter("DrawProgress", AnimatorControllerParameterType.Float);
        controller.AddParameter("VelocityX", AnimatorControllerParameterType.Float);
        controller.AddParameter("VelocityY", AnimatorControllerParameterType.Float);

        // Bool parameters
        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsAimed", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsGrounded", AnimatorControllerParameterType.Bool);

        // Trigger parameters
        controller.AddParameter("SwordSlash", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("SwordThrust", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("BowAim", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("BowDraw", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("BowRelease", AnimatorControllerParameterType.Trigger);
    }

    private static void CreateLocomotionLayer(AnimatorController controller)
    {
        AnimatorControllerLayer layer = controller.layers[0];
        layer.name = "Locomotion";

        AnimatorStateMachine stateMachine = layer.stateMachine;

        // Create states
        AnimatorState idle = stateMachine.AddState("Idle", new Vector3(300, 50, 0));
        AnimatorState swordCombat = stateMachine.AddState("SwordCombat", new Vector3(300, 150, 0));
        AnimatorState greatbowAiming = stateMachine.AddState("GreatbowAiming", new Vector3(300, 250, 0));

        stateMachine.defaultState = idle;

        // Transitions: Idle <-> SwordCombat <-> GreatbowAiming
        var idleToSword = idle.AddTransition(swordCombat);
        idleToSword.hasExitTime = false;
        idleToSword.duration = 0.1f;
        idleToSword.AddCondition(AnimatorConditionMode.Equals, 1, "CombatState");

        var swordToGreabow = swordCombat.AddTransition(greatbowAiming);
        swordToGreabow.hasExitTime = false;
        swordToGreabow.duration = 0.1f;
        swordToGreabow.AddCondition(AnimatorConditionMode.Equals, 2, "CombatState");

        var greatbowToIdle = greatbowAiming.AddTransition(idle);
        greatbowToIdle.hasExitTime = false;
        greatbowToIdle.duration = 0.1f;
        greatbowToIdle.AddCondition(AnimatorConditionMode.Equals, 0, "CombatState");

        // Also allow direct transitions back
        var swordToIdle = swordCombat.AddTransition(idle);
        swordToIdle.hasExitTime = false;
        swordToIdle.duration = 0.1f;
        swordToIdle.AddCondition(AnimatorConditionMode.Equals, 0, "CombatState");

        var greatbowToSword = greatbowAiming.AddTransition(swordCombat);
        greatbowToSword.hasExitTime = false;
        greatbowToSword.duration = 0.1f;
        greatbowToSword.AddCondition(AnimatorConditionMode.Equals, 1, "CombatState");
    }

    private static void CreateUpperBodyLayer(AnimatorController controller)
    {
        AnimatorControllerLayer upperBodyLayer = new AnimatorControllerLayer
        {
            name = "UpperBody",
            defaultWeight = 1f,
            stateMachine = new AnimatorStateMachine()
        };

        AnimatorStateMachine sm = upperBodyLayer.stateMachine;

        // Create states
        AnimatorState idle = sm.AddState("Idle", new Vector3(300, 50, 0));
        AnimatorState swordSlash = sm.AddState("SwordSlash", new Vector3(500, 50, 0));
        AnimatorState swordThrust = sm.AddState("SwordThrust", new Vector3(700, 50, 0));
        AnimatorState bowAim = sm.AddState("BowAim", new Vector3(500, 150, 0));
        AnimatorState bowDraw = sm.AddState("BowDraw", new Vector3(700, 150, 0));
        AnimatorState bowRelease = sm.AddState("BowRelease", new Vector3(900, 150, 0));

        sm.defaultState = idle;

        // Sword attack transitions
        var idleToSlash = idle.AddTransition(swordSlash);
        idleToSlash.hasExitTime = false;
        idleToSlash.duration = 0f;
        idleToSlash.AddCondition(AnimatorConditionMode.If, 0, "SwordSlash");

        var slashToIdle = swordSlash.AddTransition(idle);
        slashToIdle.hasExitTime = true;
        slashToIdle.duration = 0f;
        slashToIdle.exitTime = 0.9f;

        var idleToThrust = idle.AddTransition(swordThrust);
        idleToThrust.hasExitTime = false;
        idleToThrust.duration = 0f;
        idleToThrust.AddCondition(AnimatorConditionMode.If, 0, "SwordThrust");

        var thrustToIdle = swordThrust.AddTransition(idle);
        thrustToIdle.hasExitTime = true;
        thrustToIdle.duration = 0f;
        thrustToIdle.exitTime = 0.9f;

        // Bow transitions
        var idleToBowAim = idle.AddTransition(bowAim);
        idleToBowAim.hasExitTime = false;
        idleToBowAim.duration = 0.2f;
        idleToBowAim.AddCondition(AnimatorConditionMode.If, 0, "BowAim");

        var bowAimToIdle = bowAim.AddTransition(idle);
        bowAimToIdle.hasExitTime = false;
        bowAimToIdle.duration = 0.2f;
        bowAimToIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAimed");

        var bowAimToDraw = bowAim.AddTransition(bowDraw);
        bowAimToDraw.hasExitTime = false;
        bowAimToDraw.duration = 0f;
        bowAimToDraw.AddCondition(AnimatorConditionMode.If, 0, "BowDraw");

        var bowDrawToAim = bowDraw.AddTransition(bowAim);
        bowDrawToAim.hasExitTime = false;
        bowDrawToAim.duration = 0f;
        bowDrawToAim.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAimed");

        var bowDrawToRelease = bowDraw.AddTransition(bowRelease);
        bowDrawToRelease.hasExitTime = false;
        bowDrawToRelease.duration = 0f;
        bowDrawToRelease.AddCondition(AnimatorConditionMode.If, 0, "BowRelease");

        var bowReleaseToIdle = bowRelease.AddTransition(idle);
        bowReleaseToIdle.hasExitTime = true;
        bowReleaseToIdle.duration = 0f;
        bowReleaseToIdle.exitTime = 0.8f;

        controller.AddLayer(upperBodyLayer);
    }

    private static void CreateBowAimingLayer(AnimatorController controller)
    {
        AnimatorControllerLayer bowLayer = new AnimatorControllerLayer
        {
            name = "BowAiming",
            defaultWeight = 0f, // Start disabled
            stateMachine = new AnimatorStateMachine()
        };

        AnimatorStateMachine sm = bowLayer.stateMachine;

        // Create aiming states
        AnimatorState neutral = sm.AddState("Neutral", new Vector3(300, 50, 0));
        AnimatorState aimed = sm.AddState("Aimed", new Vector3(500, 50, 0));
        AnimatorState drawn = sm.AddState("Drawn", new Vector3(700, 50, 0));

        sm.defaultState = neutral;

        // Neutral to Aimed on BowAim trigger
        var neutralToAimed = neutral.AddTransition(aimed);
        neutralToAimed.hasExitTime = false;
        neutralToAimed.duration = 0.2f;
        neutralToAimed.AddCondition(AnimatorConditionMode.If, 0, "IsAimed");

        // Aimed to Neutral when aim is released
        var aimedToNeutral = aimed.AddTransition(neutral);
        aimedToNeutral.hasExitTime = false;
        aimedToNeutral.duration = 0.2f;
        aimedToNeutral.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAimed");

        // Aimed to Drawn on BowDraw trigger
        var aimedToDrawn = aimed.AddTransition(drawn);
        aimedToDrawn.hasExitTime = false;
        aimedToDrawn.duration = 0f;
        aimedToDrawn.AddCondition(AnimatorConditionMode.Greater, 0.1f, "DrawProgress");

        // Drawn to Aimed on charge decrease
        var drawnToAimed = drawn.AddTransition(aimed);
        drawnToAimed.hasExitTime = false;
        drawnToAimed.duration = 0f;
        drawnToAimed.AddCondition(AnimatorConditionMode.Less, 0.05f, "DrawProgress");

        // Drawn to Neutral on release
        var drawnToNeutral = drawn.AddTransition(neutral);
        drawnToNeutral.hasExitTime = false;
        drawnToNeutral.duration = 0.1f;
        drawnToNeutral.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAimed");

        controller.AddLayer(bowLayer);
    }
}
