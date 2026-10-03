using UdonSharp;
using UnityEngine;
using VRC.Udon;
using VRC.SDKBase;

public enum CharacterCombatState
{
    Idle,
    SwordCombat,
    GreatbowAiming
}

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class CharacterCombatStateMachine : UdonSharpBehaviour
{
    [Header("State")]
    [UdonSynced]
    public CharacterCombatState currentState = CharacterCombatState.Idle;

    [Header("VRChat Avatar")]
    public Animator characterAnimator;
    public VRCPlayerApi localPlayer;
    public CharacterController characterController;

    [Header("Input")]
    public KeyCode weaponSwitchKey = KeyCode.Q;
    public KeyCode attackKey = KeyCode.Mouse0;
    public KeyCode secondaryAttackKey = KeyCode.Mouse1;

    private float weaponSwitchCooldown = 0.25f;
    private float lastWeaponSwitchTime = 0f;

    [Header("Animator Parameters")]
    public string stateParameterName = "CombatState";
    public string weaponTypeParameterName = "WeaponType";
    public string drawProgressParameterName = "DrawProgress";
    public string isMovingParameterName = "IsMoving";
    public string velocityXParameterName = "VelocityX";
    public string velocityYParameterName = "VelocityY";
    public string isGroundedParameterName = "IsGrounded";
    public string isAimedParameterName = "IsAimed";

    [Header("Combat Visuals")]
    public GameObject swordVisual;
    public GameObject greatbowVisual;
    public GameObject arrowVisual;

    [Header("Sword Settings")]
    public float swordDamage = 20f;
    public float swordRange = 2.4f;
    public float swordSlashCooldown = 0.6f;
    public float swordThrustCooldown = 0.8f;
    public Transform swordAttackPoint;
    private float lastSwordAttackTime = 0f;

    [Header("Bow Settings")]
    public float bowDrawDuration = 1.2f;
    public float bowReleaseDamage = 30f;
    public float bowRange = 40f;
    public Transform bowAimPoint;
    public Transform arrowSpawnPoint;
    public float bowAimHeadLookInfluence = 0.7f;

    private float bowDrawStartTime = 0f;
    private bool isBowDrawing = false;
    private float drawProgress = 0f;

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip weaponSwitchSFX;
    public AudioClip swordSlashSFX;
    public AudioClip swordThrustSFX;
    public AudioClip bowDrawSFX;
    public AudioClip bowReleaseSFX;

    [Header("Effects")]
    public ParticleSystem swordSlashFX;
    public ParticleSystem swordThrustFX;
    public ParticleSystem bowReleaseFX;
    public ParticleSystem hitFX;

    [Header("Damage")]
    public LayerMask damageMask;

    [Header("Locomotion Settings")]
    public bool restrictMovementInBowAim = true;
    public float bowAimMovementSpeedMultiplier = 0.3f;
    public bool disableJumpInCombat = false;

    private Vector3 previousAnimatorPosition;
    private bool isMoving = false;
    private Vector3 currentMovementDirection = Vector3.zero;

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;

        if (characterAnimator == null)
            characterAnimator = GetComponent<Animator>();

        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        if (damageMask == 0)
            damageMask = LayerMask.GetMask("Default");

        previousAnimatorPosition = transform.position;
        ApplyState(currentState, true);
    }

    private void Update()
    {
        if (localPlayer == null || !Networking.IsOwner(gameObject))
            return;

        // Update locomotion parameters
        UpdateLocomotionParameters();

        // Handle weapon switch input
        if (Input.GetKeyDown(weaponSwitchKey))
        {
            if (Time.time >= lastWeaponSwitchTime + weaponSwitchCooldown)
            {
                lastWeaponSwitchTime = Time.time;
                CycleState();
            }
        }

        // Handle combat input based on current state
        switch (currentState)
        {
            case CharacterCombatState.Idle:
                break;

            case CharacterCombatState.SwordCombat:
                HandleSwordInput();
                break;

            case CharacterCombatState.GreatbowAiming:
                HandleBowInput();
                HandleBowAim();
                break;
        }

        // Update bow draw progress
        if (isBowDrawing && currentState == CharacterCombatState.GreatbowAiming)
        {
            UpdateBowDrawProgress();
        }
    }

    private void UpdateLocomotionParameters()
    {
        if (characterAnimator == null)
            return;

        // Detect movement from animator position change
        Vector3 displacement = transform.position - previousAnimatorPosition;
        float speed = displacement.magnitude / Time.deltaTime;
        isMoving = speed > 0.1f;

        // Calculate velocity in local space for blending
        Vector3 localVelocity = transform.worldToLocalMatrix.MultiplyVector(displacement / Time.deltaTime);
        float velocityX = localVelocity.x;
        float velocityY = localVelocity.z; // Forward/backward

        // Check if grounded (simplified - can be improved with raycasts)
        bool isGrounded = characterController != null ? characterController.isGrounded : true;

        // Apply movement restrictions based on combat state
        if (currentState == CharacterCombatState.GreatbowAiming && restrictMovementInBowAim)
        {
            velocityX *= bowAimMovementSpeedMultiplier;
            velocityY *= bowAimMovementSpeedMultiplier;
        }

        // Update animator
        characterAnimator.SetBool(isMovingParameterName, isMoving);
        characterAnimator.SetFloat(velocityXParameterName, velocityX);
        characterAnimator.SetFloat(velocityYParameterName, velocityY);
        characterAnimator.SetBool(isGroundedParameterName, isGrounded);
        characterAnimator.SetBool(isAimedParameterName, isBowDrawing);

        previousAnimatorPosition = transform.position;
    }

    private void HandleBowAim()
    {
        if (!isBowDrawing || bowAimPoint == null)
            return;

        // Point bow aim direction at camera look direction
        // This integrates with VRChat's head tracking
        Vector3 cameraForward = characterAnimator.GetBoneTransform(HumanBodyBones.Head).forward;
        bowAimPoint.rotation = Quaternion.LookRotation(cameraForward);

        // Optional: Adjust character upper body to face aim direction
        if (characterAnimator != null && bowAimHeadLookInfluence > 0f)
        {
            // This is handled through animator IK in your animator layer
            // The IK weight is controlled by the "IsAimed" parameter
        }
    }

    private void HandleSwordInput()
    {
        if (Input.GetKeyDown(attackKey))
        {
            if (Time.time >= lastSwordAttackTime + swordSlashCooldown)
            {
                lastSwordAttackTime = Time.time;
                ExecuteSwordSlash();
            }
        }

        if (Input.GetKeyDown(secondaryAttackKey))
        {
            if (Time.time >= lastSwordAttackTime + swordThrustCooldown)
            {
                lastSwordAttackTime = Time.time;
                ExecuteSwordThrust();
            }
        }
    }

    private void HandleBowInput()
    {
        if (Input.GetKeyDown(attackKey) && !isBowDrawing)
        {
            StartBowDraw();
        }

        if ((Input.GetKeyUp(attackKey) || Input.GetKeyDown(secondaryAttackKey)) && isBowDrawing)
        {
            ReleaseBow();
        }
    }

    public void CycleState()
    {
        // Cancel ongoing bow draw when switching states
        if (isBowDrawing)
        {
            isBowDrawing = false;
            drawProgress = 0f;
            if (arrowVisual != null)
                arrowVisual.SetActive(false);
            if (characterAnimator != null)
                characterAnimator.SetFloat(drawProgressParameterName, 0f);
        }

        switch (currentState)
        {
            case CharacterCombatState.Idle:
                currentState = CharacterCombatState.SwordCombat;
                break;

            case CharacterCombatState.SwordCombat:
                currentState = CharacterCombatState.GreatbowAiming;
                break;

            case CharacterCombatState.GreatbowAiming:
                currentState = CharacterCombatState.Idle;
                break;
        }

        ApplyState(currentState, false);
    }

    private void ApplyState(CharacterCombatState newState, bool isInitializing)
    {
        currentState = newState;

        if (characterAnimator == null)
            return;

        // Update combat state parameter
        characterAnimator.SetInteger(stateParameterName, (int)newState);

        // Update weapon type parameter
        switch (newState)
        {
            case CharacterCombatState.Idle:
                characterAnimator.SetInteger(weaponTypeParameterName, 0); // Unarmed
                break;

            case CharacterCombatState.SwordCombat:
                characterAnimator.SetInteger(weaponTypeParameterName, 1); // Sword
                break;

            case CharacterCombatState.GreatbowAiming:
                characterAnimator.SetInteger(weaponTypeParameterName, 2); // Bow
                break;
        }

        // Update weapon visuals
        if (swordVisual != null)
            swordVisual.SetActive(newState == CharacterCombatState.SwordCombat);

        if (greatbowVisual != null)
            greatbowVisual.SetActive(newState == CharacterCombatState.GreatbowAiming);

        if (arrowVisual != null)
            arrowVisual.SetActive(false);

        // Play weapon switch sound
        if (!isInitializing && weaponSwitchSFX != null && audioSource != null)
            audioSource.PlayOneShot(weaponSwitchSFX);

        // Reset locomotion parameters for new state
        characterAnimator.SetFloat(drawProgressParameterName, 0f);
        characterAnimator.SetBool(isAimedParameterName, false);

        Debug.Log($"[Combat State] Switched to: {newState}");
    }

    // ============== SWORD COMBAT ==============

    private void ExecuteSwordSlash()
    {
        if (characterAnimator != null)
            characterAnimator.SetTrigger("SwordSlash");

        if (swordSlashSFX != null && audioSource != null)
            audioSource.PlayOneShot(swordSlashSFX);

        if (swordSlashFX != null)
            swordSlashFX.Play();

        DealSwordDamage(1.0f);

        Debug.Log("[Sword] Slash executed!");
    }

    private void ExecuteSwordThrust()
    {
        if (characterAnimator != null)
            characterAnimator.SetTrigger("SwordThrust");

        if (swordThrustSFX != null && audioSource != null)
            audioSource.PlayOneShot(swordThrustSFX);

        if (swordThrustFX != null)
            swordThrustFX.Play();

        DealSwordDamage(1.35f);

        Debug.Log("[Sword] Thrust executed!");
    }

    private void DealSwordDamage(float multiplier)
    {
        if (swordAttackPoint == null)
        {
            Debug.LogWarning("Sword attack point not assigned!");
            return;
        }

        Collider[] hits = Physics.OverlapSphere(swordAttackPoint.position, swordRange * multiplier, damageMask);

        foreach (Collider col in hits)
        {
            if (col.gameObject == gameObject)
                continue;

            CharacterHealth health = col.GetComponent<CharacterHealth>();
            if (health != null)
            {
                float finalDamage = swordDamage * multiplier;
                health.TakeDamage(finalDamage, gameObject);

                if (hitFX != null)
                {
                    ParticleSystem fx = Instantiate(hitFX, col.transform.position, Quaternion.identity);
                    fx.Play();
                    Destroy(fx.gameObject, 3f);
                }

                Debug.Log($"[Damage] Dealt {finalDamage} sword damage to {col.gameObject.name}");
            }
        }
    }

    // ============== BOW COMBAT ==============

    private void StartBowDraw()
    {
        isBowDrawing = true;
        bowDrawStartTime = Time.time;
        drawProgress = 0f;

        if (arrowVisual != null)
            arrowVisual.SetActive(true);

        if (bowDrawSFX != null && audioSource != null)
            audioSource.PlayOneShot(bowDrawSFX);

        if (characterAnimator != null)
        {
            characterAnimator.SetTrigger("BowDraw");
            characterAnimator.SetBool(isAimedParameterName, true);
        }

        Debug.Log("[Bow] Draw started...");
    }

    private void UpdateBowDrawProgress()
    {
        float elapsedTime = Time.time - bowDrawStartTime;
        drawProgress = Mathf.Clamp01(elapsedTime / bowDrawDuration);

        if (characterAnimator != null)
            characterAnimator.SetFloat(drawProgressParameterName, drawProgress);

        // Visual feedback: scale arrow based on draw progress
        if (arrowVisual != null)
        {
            Vector3 scale = arrowVisual.transform.localScale;
            scale.z = 0.5f + (drawProgress * 0.5f);
            arrowVisual.transform.localScale = scale;
        }
    }

    private void ReleaseBow()
    {
        if (!isBowDrawing)
            return;

        isBowDrawing = false;

        if (characterAnimator != null)
        {
            characterAnimator.SetBool(isAimedParameterName, false);
            characterAnimator.SetTrigger("BowRelease");
        }

        if (bowReleaseSFX != null && audioSource != null)
            audioSource.PlayOneShot(bowReleaseSFX);

        if (bowReleaseFX != null)
            bowReleaseFX.Play();

        FireArrow(drawProgress);

        if (arrowVisual != null)
            arrowVisual.SetActive(false);

        drawProgress = 0f;
        if (characterAnimator != null)
            characterAnimator.SetFloat(drawProgressParameterName, 0f);

        Debug.Log($"[Bow] Released with {drawProgress * 100}% power!");
    }

    private void FireArrow(float power)
    {
        if (arrowSpawnPoint == null)
        {
            Debug.LogWarning("Arrow spawn point not assigned!");
            return;
        }

        Vector3 origin = arrowSpawnPoint.position;
        Vector3 direction = arrowSpawnPoint.forward;

        RaycastHit hit;
        if (Physics.Raycast(origin, direction, out hit, bowRange, damageMask))
        {
            if (hit.collider.gameObject == gameObject)
                return;

            CharacterHealth health = hit.collider.GetComponent<CharacterHealth>();
            if (health != null)
            {
                float finalDamage = bowReleaseDamage * Mathf.Lerp(0.5f, 1.0f, power);
                health.TakeDamage(finalDamage, gameObject);

                if (hitFX != null)
                {
                    ParticleSystem fx = Instantiate(hitFX, hit.point, Quaternion.LookRotation(hit.normal));
                    fx.Play();
                    Destroy(fx.gameObject, 3f);
                }

                Debug.Log($"[Damage] Dealt {finalDamage} arrow damage to {hit.collider.gameObject.name}");
            }
        }
    }

    // ============== PUBLIC QUERY METHODS ==============

    public bool IsInState(CharacterCombatState state)
    {
        return currentState == state;
    }

    public bool CanAttack()
    {
        return currentState == CharacterCombatState.SwordCombat || currentState == CharacterCombatState.GreatbowAiming;
    }

    public bool IsInCombat()
    {
        return currentState != CharacterCombatState.Idle;
    }

    public CharacterCombatState GetCurrentState()
    {
        return currentState;
    }

    public float GetBowDrawProgress()
    {
        return isBowDrawing ? drawProgress : 0f;
    }

    public bool IsDrawingBow()
    {
        return isBowDrawing;
    }

    public bool IsMovingInCombat()
    {
        return isMoving;
    }

    // ============== DEBUG VISUALIZATION ==============

    private void OnDrawGizmosSelected()
    {
        // Draw sword attack range
        if (swordAttackPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(swordAttackPoint.position, swordRange);
        }

        // Draw bow aim range
        if (bowAimPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(bowAimPoint.position, bowAimPoint.position + bowAimPoint.forward * bowRange);
        }

        // Draw arrow spawn point
        if (arrowSpawnPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(arrowSpawnPoint.position, 0.1f);
            Gizmos.DrawLine(arrowSpawnPoint.position, arrowSpawnPoint.position + arrowSpawnPoint.forward * 2f);
        }
    }
}
