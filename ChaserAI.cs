using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class ChaserAI : MonoBehaviour
{
    public enum AIState { Patrol, Investigate, Chase }
    
    [Header("Configuración de Estados")]
    public AIState currentState = AIState.Patrol;
    public Transform player;
    public Transform[] patrolPoints;
    
    [Header("Parámetros de Detección")]
    public float sightRange = 15f;
    public float attackRange = 2f;
    public float fieldOfViewAngle = 110f;
    public LayerMask obstacleMask; // Para que no vea a través de paredes

    private NavMeshAgent agent;
    private int currentPatrolIndex;
    private TelemetryManager telemetry; // Conexión con tu sistema de analítica

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        telemetry = FindObjectOfType<TelemetryManager>();
        GotoNextPatrolPoint();
    }

    void Update()
    {
        bool canSeePlayer = CheckLineOfSight();

        // Máquina de estados básica
        switch (currentState)
        {
            case AIState.Patrol:
                PatrolBehavior();
                if (canSeePlayer) ChangeState(AIState.Chase);
                break;

            case AIState.Investigate:
                // Lógica de ir al último sonido escuchado
                if (canSeePlayer) ChangeState(AIState.Chase);
                if (!agent.pathPending && agent.remainingDistance < 0.5f) ChangeState(AIState.Patrol);
                break;

            case AIState.Chase:
                ChaseBehavior();
                if (!canSeePlayer) ChangeState(AIState.Investigate); // Lo pierde de vista, va a donde lo vio por última vez
                break;
        }
    }

    private void ChangeState(AIState newState)
    {
        if (currentState == newState) return;

        // Disparar evento de analítica si el estado cambia a Persecución
        if (newState == AIState.Chase && telemetry != null)
        {
            string payload = $@"{{ ""chaser_distance"": {Vector3.Distance(transform.position, player.position)} }}";
            telemetry.TrackEvent("chase_started", "level_01", payload);
        }

        currentState = newState;
    }

    private void PatrolBehavior()
    {
        agent.speed = 2.5f; // Velocidad calmada
        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            GotoNextPatrolPoint();
        }
    }

    private void ChaseBehavior()
    {
        agent.speed = 5.5f; // Velocidad estresante
        agent.SetDestination(player.position);

        // Lógica si atrapa al jugador
        if (Vector3.Distance(transform.position, player.position) <= attackRange)
        {
            Debug.Log("¡Jugador Atrapado! Saturación cognitiva máxima.");
            // Aquí llamas al Game Over y envías el evento final de nivel fallido
        }
    }

    private void GotoNextPatrolPoint()
    {
        if (patrolPoints.Length == 0) return;
        agent.destination = patrolPoints[currentPatrolIndex].position;
        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
    }

    private bool CheckLineOfSight()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        if (distanceToPlayer > sightRange) return false;

        Vector3 directionToPlayer = (player.position - transform.position).normalized;
        float angleBetweenGuardAndPlayer = Vector3.Angle(transform.forward, directionToPlayer);

        if (angleBetweenGuardAndPlayer < fieldOfViewAngle / 2f)
        {
            // Raycast para comprobar que no hay paredes en medio
            if (!Physics.Raycast(transform.position + Vector3.up, directionToPlayer, distanceToPlayer, obstacleMask))
            {
                return true; // El jugador está a la vista
            }
        }
        return false;
    }
}