using UdonSharp;
using UnityEngine;
using VRC.Udon;

public enum CharacterCombatState
{
    Idle,
    SwordCombat,
    GreatbowAiming
}

public enum AttackType
{
    None,
    SwordSlash,
    SwordThrust,
    BowDraw,
    BowRelease
}

[UdonBehaviourSyncMode(BehaviourSyncMode.Continuous)]
public class CharacterCombatStateMachine : UdonSharpBehaviour
{
    [Header("State")]
    [UdonSynced]
    public CharacterCombatState currentState = CharacterCombatState.Idle;

    [Header("Input")]
    public KeyCode weaponSwitchKey = KeyCode.Q;
    public KeyCode attackKey = KeyCode.Mouse0;
    public KeyCode secondaryAttackKey = KeyCode.Mouse1;
    private float weaponSwitchCooldown = 0.3f;
    private float lastWeaponSwitchTime = 0f;

    [Header("Animator")]
    public Animator characterAnimator;
    public string stateParameterName = "CombatState";
    public string weaponTypeParameterName = "WeaponType";
    public string attackTriggerName = "Attack";
    public string secondaryAttackTriggerName = "SecondaryAttack";

    [Header("Weapon Visuals")]
    public GameObject swordVisual;
    public GameObject greatbowVisual;
    public GameObject arrowVisual;

    [Header("Sword Combat Settings")]
    public float swordSlashCooldown = 0.6f;
    public float swordThrustCooldown = 0.8f;
    public float swordDamage = 15f;
    public float swordRange = 2.5f;
    public Transform swordAttackPoint;
    private float lastSwordAttackTime = 0f;
    private AttackType lastSwordAttackType = AttackType.None;

    [Header("Bow Combat Settings")]
    public float bowDrawDuration = 1.2f;
    public float bowReleaseDamage = 25f;
    public float bowRange = 50f;
    public Transform bowAimPoint;
    public Transform arrowSpawnPoint;
    private float bowDrawStartTime = 0f;
    private bool isBowDrawing = false;
    private float drawProgress = 0f; // 0 to 1

    [Header("Audio")]
    public AudioClip swordSlashSFX;
    public AudioClip swordThrustSFX;
    public AudioClip bowDrawSFX;
    public AudioClip bowReleaseSFX;
    public AudioClip weaponSwitchSFX;
    private AudioSource audioSource;

    [Header("Effects")]
    public ParticleSystem swordSlashEffect;
    public ParticleSystem swordThrustEffect;
    public ParticleSystem bowReleaseEffect;
    public ParticleSystem hitEffect;

    [Header("VRC Settings")]
    public bool syncStateAcrossNetwork = true;
    public LayerMask damageLayerMask;

    private CharacterCombatState previousState;
    private bool canAttack = true;

    private void Start()
    {
        // Cache the animator if not assigned
        if (characterAnimator == null)
        {
            characterAnimator = GetComponent<Animator>();
        }

        // Try to get or create AudioSource
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        if (damageLayerMask == 0)
        {
            damageLayerMask = LayerMask.GetMask("Default");
        }

        previousState = currentState;
        ApplyState(currentState, true);
    }

    private void Update()
    {
        // Only allow local input
        if (!Networking.IsOwner(gameObject))
            return;

        // Check for weapon switch input with cooldown
        if (Input.GetKeyDown(weaponSwitchKey))
        {
            if (Time.time >= lastWeaponSwitchTime + weaponSwitchCooldown)
            {
                lastWeaponSwitchTime = Time.time;
                RequestCycleState();
            }
        }

        // Handle combat input based on current state
        HandleCombatInput();

        // Update bow draw progress if aiming
        if (isBowDrawing && currentState == CharacterCombatState.GreatbowAiming)
        {
            UpdateBowDrawProgress();
        }
    }

    private void HandleCombatInput()
    {
        switch (currentState)
        {
            case CharacterCombatState.SwordCombat:
                HandleSwordInput();
                break;

            case CharacterCombatState.GreatbowAiming:
                HandleBowInput();
                break;

            case CharacterCombatState.Idle:
                // No combat in idle
                break;
        }
    }

    private void HandleSwordInput()
    {
        // Primary attack: Slash
        if (Input.GetKeyDown(attackKey))
        {
            if (Time.time >= lastSwordAttackTime + swordSlashCooldown)
            {
                RequestSwordAttack(AttackType.SwordSlash);
            }
        }

        // Secondary attack: Thrust
        if (Input.GetKeyDown(secondaryAttackKey))
        {
            if (Time.time >= lastSwordAttackTime + swordThrustCooldown)
            {
                RequestSwordAttack(AttackType.SwordThrust);
            }
        }
    }

    private void HandleBowInput()
    {
        // Start drawing bow on primary attack
        if (Input.GetKeyDown(attackKey) && !isBowDrawing)
        {
            StartBowDraw();
        }

        // Release arrow when button is released or secondary attack pressed
        if ((Input.GetKeyUp(attackKey) || Input.GetKeyDown(secondaryAttackKey)) && isBowDrawing)
        {
            ReleaseBowArrow();
        }
    }

    public void RequestCycleState()
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
        // Cancel any ongoing attacks when switching states
        if (isBowDrawing)
        {
            isBowDrawing = false;
            if (arrowVisual != null)
                arrowVisual.SetActive(false);
        }

        previousState = currentState;

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

        // Update Animator Parameters
        UpdateAnimator(newState);

        // Update Weapon Visuals
        UpdateWeaponVisuals(newState);

        // Play Audio
        if (!isInitializing && weaponSwitchSFX != null)
        {
            audioSource.PlayOneShot(weaponSwitchSFX);
        }

        // Debug Log
        Debug.Log($"[Combat State Machine] Switched to: {newState}");
    }

    private void UpdateAnimator(CharacterCombatState newState)
    {
        if (characterAnimator == null)
            return;

        // Set the state parameter (integer value 0, 1, 2)
        int stateValue = (int)newState;
        characterAnimator.SetInteger(stateParameterName, stateValue);

        // Also set weapon type for more granular control
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
    }

    private void UpdateWeaponVisuals(CharacterCombatState newState)
    {
        // Disable all weapons first
        if (swordVisual != null)
            swordVisual.SetActive(false);

        if (greatbowVisual != null)
            greatbowVisual.SetActive(false);

        if (arrowVisual != null)
            arrowVisual.SetActive(false);

        // Enable the appropriate weapon
        switch (newState)
        {
            case CharacterCombatState.Idle:
                // No weapon active in Idle state
                break;

            case CharacterCombatState.SwordCombat:
                if (swordVisual != null)
                    swordVisual.SetActive(true);
                break;

            case CharacterCombatState.GreatbowAiming:
                if (greatbowVisual != null)
                    greatbowVisual.SetActive(true);
                break;
        }
    }

    // ============== SWORD COMBAT ==============

    public void RequestSwordAttack(AttackType attackType)
    {
        if (syncStateAcrossNetwork)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.AllBuffered, nameof(ExecuteSwordAttack));
        }
        else
        {
            ExecuteSwordAttack();
        }

        lastSwordAttackType = attackType;
        lastSwordAttackTime = Time.time;
    }

    public void ExecuteSwordAttack()
    {
        if (currentState != CharacterCombatState.SwordCombat)
            return;

        // Determine attack type based on time since last attack
        AttackType attackType = lastSwordAttackType;
        string animTrigger = (attackType == AttackType.SwordSlash) ? "SwordSlash" : "SwordThrust";
        AudioClip sfx = (attackType == AttackType.SwordSlash) ? swordSlashSFX : swordThrustSFX;
        ParticleSystem effect = (attackType == AttackType.SwordSlash) ? swordSlashEffect : swordThrustEffect;

        // Play animation
        if (characterAnimator != null)
        {
            characterAnimator.SetTrigger(animTrigger);
        }

        // Play sound
        if (sfx != null && audioSource != null)
        {
            audioSource.PlayOneShot(sfx);
        }

        // Play effect
        if (effect != null)
        {
            effect.Play();
        }

        // Perform damage check
        PerformSwordDamage(attackType);

        Debug.Log($"[Sword Attack] {attackType} executed!");
    }

    private void PerformSwordDamage(AttackType attackType)
    {
        if (swordAttackPoint == null)
        {
            Debug.LogWarning("Sword attack point not assigned!");
            return;
        }

        // Raycast or sphere cast to detect enemies
        Collider[] hitColliders = Physics.OverlapSphere(swordAttackPoint.position, swordRange, damageLayerMask);

        foreach (Collider hitCollider in hitColliders)
        {
            // Skip self
            if (hitCollider.gameObject == gameObject)
                continue;

            // Try to get the character controller or health component
            UdonBehaviour targetBehaviour = hitCollider.GetComponent<UdonBehaviour>();
            CharacterHealth targetHealth = hitCollider.GetComponent<CharacterHealth>();

            if (targetHealth != null)
            {
                targetHealth.TakeDamage(swordDamage, gameObject);
                Debug.Log($"[Damage] Dealt {swordDamage} sword damage to {hitCollider.gameObject.name}");

                // Play hit effect
                if (hitEffect != null)
                {
                    Instantiate(hitEffect, hitCollider.transform.position, Quaternion.identity);
                }
            }
        }
    }

    // ============== BOW COMBAT ==============

    private void StartBowDraw()
    {
        isBowDrawing = true;
        bowDrawStartTime = Time.time;
        drawProgress = 0f;

        // Show arrow
        if (arrowVisual != null)
        {
            arrowVisual.SetActive(true);
        }

        // Play draw sound
        if (bowDrawSFX != null && audioSource != null)
        {
            audioSource.PlayOneShot(bowDrawSFX);
        }

        // Play draw animation
        if (characterAnimator != null)
        {
            characterAnimator.SetTrigger("BowDraw");
        }

        Debug.Log("[Bow] Started drawing...");
    }

    private void UpdateBowDrawProgress()
    {
        float elapsedTime = Time.time - bowDrawStartTime;
        drawProgress = Mathf.Clamp01(elapsedTime / bowDrawDuration);

        // Update animator with draw progress
        if (characterAnimator != null)
        {
            characterAnimator.SetFloat("DrawProgress", drawProgress);
        }

        // Visual feedback: scale arrow based on draw progress
        if (arrowVisual != null)
        {
            Vector3 scale = arrowVisual.transform.localScale;
            scale.z = 0.5f + (drawProgress * 0.5f); // Scale from 0.5 to 1.0
            arrowVisual.transform.localScale = scale;
        }
    }

    private void ReleaseBowArrow()
    {
        if (!isBowDrawing)
            return;

        isBowDrawing = false;

        // Calculate damage based on draw progress
        float damageMultiplier = Mathf.Lerp(0.3f, 1.0f, drawProgress);
        float finalDamage = bowReleaseDamage * damageMultiplier;

        // Play release sound
        if (bowReleaseSFX != null && audioSource != null)
        {
            audioSource.PlayOneShot(bowReleaseSFX);
        }

        // Play release animation
        if (characterAnimator != null)
        {
            characterAnimator.SetTrigger("BowRelease");
        }

        // Play effect
        if (bowReleaseEffect != null)
        {
            bowReleaseEffect.Play();
        }

        // Fire the arrow
        FireArrow(finalDamage, drawProgress);

        // Hide arrow
        if (arrowVisual != null)
        {
            arrowVisual.SetActive(false);
        }

        Debug.Log($"[Bow] Released arrow with {drawProgress * 100}% power, damage: {finalDamage}");
    }

    private void FireArrow(float damage, float drawStrength)
    {
        if (arrowSpawnPoint == null)
        {
            Debug.LogWarning("Arrow spawn point not assigned!");
            return;
        }

        // Raycast in the direction the bow is aiming
        Vector3 rayDirection = arrowSpawnPoint.forward;
        float distance = bowRange * Mathf.Lerp(0.5f, 1.0f, drawStrength);

        RaycastHit[] hits = Physics.RaycastAll(arrowSpawnPoint.position, rayDirection, distance, damageLayerMask);

        // Sort by distance and hit the closest target
        if (hits.Length > 0)
        {
            RaycastHit closestHit = hits[0];
            float closestDistance = closestHit.distance;

            foreach (RaycastHit hit in hits)
            {
                if (hit.distance < closestDistance && hit.collider.gameObject != gameObject)
                {
                    closestHit = hit;
                    closestDistance = hit.distance;
                }
            }

            // Apply damage
            if (closestHit.collider.gameObject != gameObject)
            {
                CharacterHealth targetHealth = closestHit.collider.GetComponent<CharacterHealth>();

                if (targetHealth != null)
                {
                    targetHealth.TakeDamage(damage, gameObject);
                    Debug.Log($"[Damage] Dealt {damage} arrow damage to {closestHit.collider.gameObject.name}");

                    // Play hit effect at impact point
                    if (hitEffect != null)
                    {
                        Instantiate(hitEffect, closestHit.point, Quaternion.identity);
                    }
                }
            }
        }
    }

    // ============== STATE QUERIES ==============

    public bool IsInState(CharacterCombatState state)
    {
        return currentState == state;
    }

    public bool CanAttack()
    {
        return currentState == CharacterCombatState.SwordCombat || currentState == CharacterCombatState.GreatbowAiming;
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

    public void SetStateDirectly(CharacterCombatState newState)
    {
        if (Networking.IsOwner(gameObject))
        {
            previousState = currentState;
            currentState = newState;
            ApplyState(newState, false);

            if (syncStateAcrossNetwork)
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.AllBuffered, nameof(OnNetworkStateUpdate));
            }
        }
    }

    public void OnNetworkStateUpdate()
    {
        ApplyState(currentState, false);
    }

    // Helper method to draw gizmos for attack ranges
    private void OnDrawGizmosSelected()
    {
        // Draw sword attack range
        if (swordAttackPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(swordAttackPoint.position, swordRange);
        }

        // Draw bow range
        if (bowAimPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(bowAimPoint.position, bowAimPoint.position + bowAimPoint.forward * bowRange);
        }
    }
}
