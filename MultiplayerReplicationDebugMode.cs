using System.Collections.Generic;
using UnityEngine;

internal sealed class MultiplayerReplicationDebugMode : MonoBehaviour
{
    private const int CircleSegments = 96;
    private const float CameraSpeed = 18f;
    private const float FastCameraSpeed = 54f;
    private readonly List<Vector2> playerPositions = new List<Vector2>();
    private readonly List<LineRenderer> worldCircles = new List<LineRenderer>();
    private readonly List<LineRenderer> npcCircles = new List<LineRenderer>();
    private bool enabledMode;
    private Transform freeCameraTarget;
    private Transform previousCameraTarget;
    private Rigidbody2D frozenBody;
    private RigidbodyConstraints2D frozenConstraints;

    internal void Toggle()
    {
        enabledMode = !enabledMode;
        if (enabledMode) EnableMode();
        else DisableMode();
    }

    private void OnDisable()
    {
        DisableMode();
    }

    private void Update()
    {
        if (!enabledMode) return;
        if (!MultiplayerSession.IsConnected)
        {
            DisableMode();
            return;
        }
        EnsureFrozenBody();
        UpdateFreeCamera();
        UpdateCircles();
    }

    private void FixedUpdate()
    {
        if (!enabledMode || frozenBody == null) return;
        frozenBody.velocity = Vector2.zero;
        frozenBody.angularVelocity = 0f;
    }

    private void EnableMode()
    {
        var camera = CameraFollow.cam;
        var body = LocalBody();
        if (camera == null || body == null)
        {
            enabledMode = false;
            MultiplayerHud.AddSystemMessage("LOD debug unavailable");
            return;
        }
        previousCameraTarget = camera.target;
        var targetObject = new GameObject("MultiplayerDebugFreeCamera");
        freeCameraTarget = targetObject.transform;
        freeCameraTarget.position = body.transform.position;
        camera.target = freeCameraTarget;
        EnsureFrozenBody();
        if (MultiplayerSession.IsHost && MultiplayerHud.Instance != null)
            MultiplayerHud.Instance.SetReplicationDebugOverlay(true);
        MultiplayerHud.AddSystemMessage("LOD debug ON  WASD move  Shift fast");
    }

    private void DisableMode()
    {
        if (!enabledMode && freeCameraTarget == null && frozenBody == null) return;
        enabledMode = false;
        RestoreFrozenBody();
        if (CameraFollow.cam != null && freeCameraTarget != null && CameraFollow.cam.target == freeCameraTarget)
            CameraFollow.cam.target = previousCameraTarget ?? LocalBodyTransform();
        if (freeCameraTarget != null) Destroy(freeCameraTarget.gameObject);
        freeCameraTarget = null;
        previousCameraTarget = null;
        HideCircles();
        if (MultiplayerSession.IsHost && MultiplayerHud.Instance != null)
            MultiplayerHud.Instance.SetReplicationDebugOverlay(false);
        MultiplayerHud.AddSystemMessage("LOD debug OFF");
    }

    private void EnsureFrozenBody()
    {
        var body = LocalBody();
        var rigidbody = body == null ? null : body.rb;
        if (rigidbody == frozenBody) return;
        RestoreFrozenBody();
        if (rigidbody == null) return;
        frozenBody = rigidbody;
        frozenConstraints = rigidbody.constraints;
        rigidbody.velocity = Vector2.zero;
        rigidbody.angularVelocity = 0f;
        rigidbody.constraints = RigidbodyConstraints2D.FreezeAll;
    }

    private void RestoreFrozenBody()
    {
        if (frozenBody != null) frozenBody.constraints = frozenConstraints;
        frozenBody = null;
    }

    private void UpdateFreeCamera()
    {
        if (freeCameraTarget == null) return;
        var movement = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (movement.sqrMagnitude > 1f) movement.Normalize();
        var speed = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? FastCameraSpeed : CameraSpeed;
        freeCameraTarget.position += (Vector3)(movement * speed * Time.unscaledDeltaTime);
    }

    private void UpdateCircles()
    {
        MultiplayerLoadDistance.CopyPlayerPositions(playerPositions);
        SetCircleCount(worldCircles, playerPositions.Count, new Color(0.15f, 0.75f, 1f, 0.8f));
        SetCircleCount(npcCircles, playerPositions.Count, new Color(1f, 0.75f, 0.15f, 0.8f));
        var worldRadius = Mathf.Sqrt(MultiplayerLoadDistance.WorldDistanceSqr);
        var npcRadius = Mathf.Sqrt(MultiplayerLoadDistance.NpcPoseDistanceSqr);
        for (var index = 0; index < playerPositions.Count; index++)
        {
            DrawCircle(worldCircles[index], playerPositions[index], worldRadius);
            DrawCircle(npcCircles[index], playerPositions[index], npcRadius);
        }
    }

    private static void DrawCircle(LineRenderer line, Vector2 center, float radius)
    {
        for (var index = 0; index <= CircleSegments; index++)
        {
            var angle = index * Mathf.PI * 2f / CircleSegments;
            line.SetPosition(index, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
        }
    }

    private static void SetCircleCount(List<LineRenderer> circles, int count, Color color)
    {
        while (circles.Count < count)
        {
            var circle = new GameObject("MultiplayerDebugLodCircle").AddComponent<LineRenderer>();
            circle.material = new Material(Shader.Find("Sprites/Default"));
            circle.useWorldSpace = true;
            circle.positionCount = CircleSegments + 1;
            circle.startWidth = 0.045f;
            circle.endWidth = 0.045f;
            circle.startColor = color;
            circle.endColor = color;
            circle.sortingOrder = short.MaxValue;
            circles.Add(circle);
        }
        for (var index = 0; index < circles.Count; index++)
            if (circles[index] != null) circles[index].gameObject.SetActive(index < count);
    }

    private void HideCircles()
    {
        foreach (var circle in worldCircles)
            if (circle != null) circle.gameObject.SetActive(false);
        foreach (var circle in npcCircles)
            if (circle != null) circle.gameObject.SetActive(false);
    }

    private static BodyScript LocalBody()
    {
        var player = PlayerScript.player;
        return player == null ? null : player.bodyScript;
    }

    private static Transform LocalBodyTransform()
    {
        var body = LocalBody();
        return body == null ? null : body.transform;
    }
}
