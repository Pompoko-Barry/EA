using TMPro;
using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

/// <summary>
/// One rat in the race. Put this on your rat prefab.
///
/// - Always moves right at a slow constant speed.
/// - GoFaster: short burst of speed. Rat is VULNERABLE while boosting.
/// - Attack:   tries to stun another rat. Attacker is VULNERABLE while attacking.
/// - Defend:   invulnerable to attacks for a short time.
///
/// Any rat that isn't defending can be stunned. Vulnerable rats (boosting or
/// attacking) get stunned for longer (see vulnerableStunMultiplier).
/// Stunned rats can't move or use commands.
/// </summary>
public class RatController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float baseSpeed = 1f;

    [Header("1. Go Faster")]
    [SerializeField] float boostSpeed = 4f;
    [SerializeField] float boostDuration = 1f;
    [SerializeField] float boostCooldown = 3f;

    [Header("2. Attack")]
    [Tooltip("Max distance to the target. 0 = can attack anyone.")]
    [SerializeField] float attackRange = 0f;
    [Tooltip("How long the attacker stays vulnerable after attacking.")]
    [SerializeField] float attackDuration = 0.75f;
    [SerializeField] float stunDuration = 2f;
    [SerializeField] float attackCooldown = 4f;

    [Header("3. Defend")]
    [SerializeField] float defendDuration = 2f;
    [SerializeField] float defendCooldown = 5f;

    [Header("Vulnerability")]
    [Tooltip("Stun length is multiplied by this if the target is boosting or attacking. Set to 1 to turn off.")]
    [SerializeField] float vulnerableStunMultiplier = 1.5f;

    [Header("Visuals (optional)")]
    public TMP_Text nameLabel;
    [SerializeField] SpriteRenderer spriteRenderer;
    [SerializeField] Color boostColor = new Color(1f, 0.9f, 0.3f);
    [SerializeField] Color attackColor = new Color(1f, 0.4f, 0.4f);
    [SerializeField] Color defendColor = new Color(0.4f, 0.8f, 1f);
    [SerializeField] Color stunColor = new Color(0.5f, 0.5f, 0.5f);

    [Header("Events (hook up animations / sounds)")]
    public UnityEvent OnBoost;
    public UnityEvent OnAttack;
    public UnityEvent OnDefend;
    public UnityEvent OnStunned;
    public UnityEvent OnBlockedAttack;

    public string Username { get; private set; }

    float boostTimer, attackTimer, defendTimer, stunTimer;
    float boostCdTimer, attackCdTimer, defendCdTimer;
    Color normalColor = Color.white;

    public bool IsBoosting => boostTimer > 0f;
    public bool IsAttacking => attackTimer > 0f;
    public bool IsDefending => defendTimer > 0f;
    public bool IsStunned => stunTimer > 0f;
    public bool IsVulnerable => IsBoosting || IsAttacking;

    // Eb script
    public bool isActive = false;

    void Awake()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null) normalColor = spriteRenderer.color;
    }

    public void Init(string username)
    {
        Username = username;
        gameObject.name = "Rat_" + username;
        if (nameLabel != null) nameLabel.text = username;
    }

    void Update()
    {
        if (isActive) {
        float dt = Time.deltaTime;

        boostTimer = Mathf.Max(0f, boostTimer - dt);
        attackTimer = Mathf.Max(0f, attackTimer - dt);
        defendTimer = Mathf.Max(0f, defendTimer - dt);
        stunTimer = Mathf.Max(0f, stunTimer - dt);
        boostCdTimer = Mathf.Max(0f, boostCdTimer - dt);
        attackCdTimer = Mathf.Max(0f, attackCdTimer - dt);
        defendCdTimer = Mathf.Max(0f, defendCdTimer - dt);

        if (!IsStunned)
        {
            float speed = IsBoosting ? boostSpeed : baseSpeed;
            transform.Translate(Vector3.right * speed * dt, Space.World);
        }

        UpdateVisuals();
        }
    }

    // ---------------------------------------------------------------------
    // Commands (called by Rat_Input). Each returns true if it actually happened.
    // ---------------------------------------------------------------------

    /// 1. Short burst of speed. Vulnerable while boosting.
    public bool GoFaster()
    {
        if (IsStunned || boostCdTimer > 0f) return false;

        defendTimer = 0f; // can't hide while sprinting
        boostTimer = boostDuration;
        boostCdTimer = boostCooldown;
        OnBoost?.Invoke();
        return true;
    }

    /// 2. Try to stun another rat. The attacker is vulnerable for attackDuration.
    /// landed = true if the target was stunned, false if they were defending.
    public bool Attack(RatController target, out bool landed)
    {
        landed = false;
        if (target == null || target == this) return false;
        if (IsStunned || attackCdTimer > 0f) return false;
        if (attackRange > 0f && Vector3.Distance(transform.position, target.transform.position) > attackRange)
            return false;

        defendTimer = 0f; // attacking drops your guard
        attackTimer = attackDuration;
        attackCdTimer = attackCooldown;
        OnAttack?.Invoke();

        landed = target.ReceiveAttack(this);
        return true;
    }

    /// 3. Invulnerable to attacks for defendDuration.
    public bool Defend()
    {
        if (IsStunned || defendCdTimer > 0f) return false;

        boostTimer = 0f;
        attackTimer = 0f;
        defendTimer = defendDuration;
        defendCdTimer = defendCooldown;
        OnDefend?.Invoke();
        return true;
    }

    /// Called on the target by an attacking rat. Returns true if the stun landed.
    public bool ReceiveAttack(RatController attacker)
    {
        if (IsDefending)
        {
            OnBlockedAttack?.Invoke();
            return false;
        }

        float duration = attacker.stunDuration;
        if (IsVulnerable) duration *= vulnerableStunMultiplier;

        stunTimer = Mathf.Max(stunTimer, duration);
        boostTimer = 0f;
        attackTimer = 0f;
        OnStunned?.Invoke();
        return true;
    }

    // ---------------------------------------------------------------------

    // ---------------------------------------------------------------------
    // Finding the leader
    // ---------------------------------------------------------------------

    /// How far this rat still has to go to reach finishX (x-axis only).
    /// Negative means it has already crossed the line.
    public float DistanceToFinish(float finishX)
    {
        return finishX - transform.position.x;
    }

    /// Returns the rat closest to the finish line on the x-axis that hasn't
    /// crossed it yet, skipping "exclude" (the attacker). Null if none.
    public static RatController FindClosestToFinish(IEnumerable<RatController> rats, float finishX, RatController exclude = null)
    {
        RatController best = null;
        float bestDistance = float.MaxValue;

        foreach (RatController r in rats)
        {
            if (r == null || r == exclude) continue;

            float d = r.DistanceToFinish(finishX);
            if (d < 0f) continue; // already finished, can't be attacked

            if (d < bestDistance)
            {
                bestDistance = d;
                best = r;
            }
        }

        return best;
    }

    void UpdateVisuals()
    {
        if (spriteRenderer == null) return;

        if (IsStunned) spriteRenderer.color = stunColor;
        else if (IsDefending) spriteRenderer.color = defendColor;
        else if (IsAttacking) spriteRenderer.color = attackColor;
        else if (IsBoosting) spriteRenderer.color = boostColor;
        else spriteRenderer.color = normalColor;
    }
}