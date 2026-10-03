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
    [Header("═══════════════════════════════════")]
    [Header("DRAG & DROP SETUP")]
    [Header("═══════════════════════════════════")]
    [Header("1. Drag your character Avatar here:")]
    public GameObject characterRoot;

    [Header("2. Drag weapon visual objects (optional):")]
    public GameObject swordVisual;
    public GameObject greatbowVisual;
    public GameObject arrowVisual;

    [Header("3. Drag attack point transforms:")]
    public Transform swordAttackPoint;
    public Transform bowAimPoint;
    public Transform arrowSpawnPoint;

    [Header("4. Assign audio clips:")]
    public AudioClip weaponSwitchSFX;
    public AudioClip swordSlashSFX;
    public AudioClip swordThrustSFX;
    public AudioClip bowDrawSFX;
    public AudioClip bowReleaseSFX;

    [Header("5. Assign particle effects (optional):")]
    public ParticleSystem swordSlashFX;
    public ParticleSystem swordThrustFX;
    public ParticleSystem bowReleaseFX;
    public ParticleSystem hitFX;

    [Header("═══════════════════════════════════")]
    [Header("STATE & INPUT")]
    [Header("═══════════════════════════════════")]
    [UdonSynced]
    public CharacterCombatState currentState = CharacterCombatState.Idle;

    [Header("Input Settings")]
    public KeyCode weaponSwitchKey = KeyCode.Q;
    public KeyCode attackKey = KeyCode.Mouse0;
    public KeyCode secondaryAttackKey = KeyCode.Mouse1;

    [Header("═══════════════════════════════════")]
    [Header("COMBAT BALANCE")]
    [Header("═══════════════════════════════════")]
    [Header("Sword Settings")]
    [Range(1f, 100f)] public float swordDamage = 20f;
    [Range(0.5f, 5f)] public float swordRange = 2.4f;
    [Range(0.2f, 2f)] public float swordSlashCooldown = 0.6f;
    [Range(0.2f, 2f)] public float swordThrustCooldown = 0.8f;

    [Header("Bow Settings")]
    [Range(0.5f, 3f)] public float bowDrawDuration = 1.2f;
    [Range(5f, 100f)] public float bowReleaseDamage = 30f;
    [Range(10f, 100f)] public float bowRange = 40f;
    [Range(0.1f, 1f)] public float bowAimMovementSpeedMultiplier = 0.3f;

    [Header("═══════════════════════════════════")]
    [Header("ADVANCED OPTIONS")]
    [Header("═══════════════════════════════════")]
    [Header("Animator Parameter Names")]
    public string stateParameterName = "CombatState";
    public string weaponTypeParameterName = "WeaponType";
    public string drawProgressParameterName = "DrawProgress";
    public string isMovingParameterName = "IsMoving";
    public string velocityXParameterName = "VelocityX";
    public string velocityYParameterName = "VelocityY";
    public string isGroundedParameterName = "IsGrounded";
    public string isAimedParameterName = "IsAimed";

    [Header("Behavior Flags")]
    public bool restrictMovementInBowAim = true;
    public bool syncStateAcrossNetwork = true;
    public LayerMask damageMask = -1; // All layers by default

    // ============== INTERNAL STATE ==============
    private Animator characterAnimator;
    private AudioSource audioSource;
    private CharacterController characterController;

    private float lastWeaponSwitchTime = 0f;
    private float weaponSwitchCooldown = 0.25f;
    private float lastSwordAttackTime = 0f;

    private float bowDrawStartTime = 0f;
    private bool isBowDrawing = false;
    private float drawProgress = 0f;

    private Vector3 previousPosition;
    private bool isMoving = false;

    private void OnEnable()
    {
        // Auto-setup on scene load
        AutoSetupComponents();
    }

    private void AutoSetupComponents()
    {
        // Find animator
        if (characterAnimator == null)
        {
            if (characterRoot != null)
                characterAnimator = characterRoot.GetComponent<Animator>();
            
            if (characterAnimator == null)
                characterAnimator = GetComponent<Animator>();
        }

        // Find audio source
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }

        // Find character controller
        if (characterController == null)
            characterController = GetComponent<CharacterController>();

        // Set default damage mask
        if (damageMask == 0)
            damageMask = LayerMask.GetMask("Default");

        previousPosition = transform.position;
    }

    private void Start()
    {
        AutoSetupComponents();
        ApplyState(currentState, true);
    }

    private void Update()
    {
        if (!Networking.IsOwner(gameObject))
            return;

        UpdateLocomotionInputs();

        // Weapon switch
        if (Input.GetKeyDown(weaponSwitchKey))
        {
            if (Time.time >= lastWeaponSwitchTime + weaponSwitchCooldown)
            {
                lastWeaponSwitchTime = Time.time;
                RequestCycleState();
            }
        }

        // Combat input
        switch (currentState)
        {
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

    private void UpdateLocomotionInputs()
    {
        if (characterAnimator == null)
            return;

        // Calculate movement
        Vector3 displacement = transform.position - previousPosition;
        float speed = displacement.magnitude / Time.deltaTime;
        isMoving = speed > 0.1f;

        Vector3 localVelocity = transform.worldToLocalMatrix.MultiplyVector(displacement / Time.deltaTime);
        float velocityX = localVelocity.x;
        float velocityY = localVelocity.z;

        // Restrict movement in bow aim
        if (currentState == CharacterCombatState.GreatbowAiming && restrictMovementInBowAim)
        {
            velocityX *= bowAimMovementSpeedMultiplier;
            velocityY *= bowAimMovementSpeedMultiplier;
        }

        // Update animator
        characterAnimator.SetBool(isMovingParameterName, isMoving);
        characterAnimator.SetFloat(velocityXParameterName, velocityX);
        characterAnimator.SetFloat(velocityYParameterName, velocityY);
        characterAnimator.SetBool(isGroundedParameterName, characterController != null ? characterController.isGrounded : true);
        characterAnimator.SetBool(isAimedParameterName, isBowDrawing);

        previousPosition = transform.position;
    }

    private void HandleSwordInput()
    {
        if (Input.GetKeyDown(attackKey))
        {
            if (Time.time >= lastSwordAttackTime + swordSlashCooldown)
            {
                lastSwordAttackTime = Time.time;
                RequestSwordAttack("Slash");
            }
        }

        if (Input.GetKeyDown(secondaryAttackKey))
        {
            if (Time.time >= lastSwordAttackTime + swordThrustCooldown)
            {
                lastSwordAttackTime = Time.time;
                RequestSwordAttack("Thrust");
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

    private void HandleBowAim()
    {
        if (!isBowDrawing || bowAimPoint == null || characterAnimator == null)
            return;

        Transform head = characterAnimator.GetBoneTransform(HumanBodyBones.Head);
        if (head != null)
        {
            Vector3 aimDirection = head.forward;
            bowAimPoint.rotation = Quaternion.LookRotation(aimDirection, Vector3.up);
        }
    }

    private void RequestCycleState()
    {
        if (syncStateAcrossNetwork)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.AllBuffered, nameof(CycleState));
        }
        else
        {
            CycleState();
        }
    }

    public void CycleState()
    {
        // Cancel bow draw on state change
        if (isBowDrawing)
        {
            isBowDrawing = false;
            if (arrowVisual != null)
                arrowVisual.SetActive(false);
            if (characterAnimator != null)
                characterAnimator.SetFloat(drawProgressParameterName, 0f);
        }

        // Cycle state
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

        if (characterAnimator != null)
        {
            characterAnimator.SetInteger(stateParameterName, (int)newState);

            switch (newState)
            {
                case CharacterCombatState.Idle:
                    characterAnimator.SetInteger(weaponTypeParameterName, 0);
                    break;

                case CharacterCombatState.SwordCombat:
                    characterAnimator.SetInteger(weaponTypeParameterName, 1);
                    break;

                case CharacterCombatState.GreatbowAiming:
                    characterAnimator.SetInteger(weaponTypeParameterName, 2);
                    break;
            }

            characterAnimator.SetFloat(drawProgressParameterName, 0f);
            characterAnimator.SetBool(isAimedParameterName, false);
        }

        // Update visuals
        if (swordVisual != null)
            swordVisual.SetActive(newState == CharacterCombatState.SwordCombat);

        if (greatbowVisual != null)
            greatbowVisual.SetActive(newState == CharacterCombatState.GreatbowAiming);

        if (arrowVisual != null)
            arrowVisual.SetActive(false);

        // Play sound
        if (!isInitializing && weaponSwitchSFX != null && audioSource != null)
        {
            audioSource.PlayOneShot(weaponSwitchSFX);
        }

        isBowDrawing = false;
        drawProgress = 0f;

        Debug.Log($"[Combat State] Switched to: {newState}");
    }

    private void RequestSwordAttack(string attackType)
    {
        if (syncStateAcrossNetwork)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.AllBuffered, nameof(ExecuteSwordAttack));
        }
        else
        {
            ExecuteSwordAttack();
        }
    }

    public void ExecuteSwordAttack()
    {
        if (currentState != CharacterCombatState.SwordCombat)
            return;

        // Determine if slash or thrust (last pressed)
        float timeSinceSlashWindow = Time.time - lastSwordAttackTime;
        bool isThrust = timeSinceSlashWindow < swordThrustCooldown;

        string triggerName = isThrust ? "SwordThrust" : "SwordSlash";
        AudioClip sfx = isThrust ? swordThrustSFX : swordSlashSFX;
        ParticleSystem effect = isThrust ? swordThrustFX : swordSlashFX;
        float damageMultiplier = isThrust ? 1.35f : 1.0f;

        // Play animation
        if (characterAnimator != null)
            characterAnimator.SetTrigger(triggerName);

        // Play sound
        if (sfx != null && audioSource != null)
            audioSource.PlayOneShot(sfx);

        // Play effect
        if (effect != null)
            effect.Play();

        // Deal damage
        DealSwordDamage(damageMultiplier);
    }

    private void DealSwordDamage(float multiplier)
    {
        if (swordAttackPoint == null)
        {
            Debug.LogWarning("[Combat] Sword attack point not assigned!");
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
                }
            }
        }
    }

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
    }

    private void UpdateBowDrawProgress()
    {
        float elapsedTime = Time.time - bowDrawStartTime;
        drawProgress = Mathf.Clamp01(elapsedTime / bowDrawDuration);

        if (characterAnimator != null)
            characterAnimator.SetFloat(drawProgressParameterName, drawProgress);

        // Visual feedback
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
    }

    private void FireArrow(float power)
    {
        if (arrowSpawnPoint == null)
        {
            Debug.LogWarning("[Combat] Arrow spawn point not assigned!");
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
                }
            }
        }
    }

    // ============== PUBLIC QUERIES ==============

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

    // ============== DEBUG VISUALIZATION ==============

    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying)
        {
            // Draw sword attack range
            if (swordAttackPoint != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(swordAttackPoint.position, swordRange);
            }

            // Draw bow aim line
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
}

// ============== SIMPLE HEALTH COMPONENT ==============

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class CharacterHealth : UdonSharpBehaviour
{
    [Header("Health Settings")]
    [Range(1f, 500f)] public float maxHealth = 100f;
    
    [UdonSynced]
    public float currentHealth = 100f;

    private bool isDead = false;

    private void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount, GameObject damageSource)
    {
        if (isDead)
            return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);

        Debug.Log($"[Health] {gameObject.name} took {amount} damage. Health: {currentHealth}/{maxHealth}");

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;
        Debug.Log($"[Health] {gameObject.name} was defeated!");

        // You can add death behavior here (animations, sounds, etc)
    }

    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }

    public void ResetHealth()
    {
        currentHealth = maxHealth;
        isDead = false;
    }

    public float GetHealthPercent()
    {
        return currentHealth / maxHealth;
    }
}
