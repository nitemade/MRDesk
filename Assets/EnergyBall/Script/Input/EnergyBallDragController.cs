using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Scripting.APIUpdating;

namespace EnergyBall
{
    /// <summary>
    /// 编辑器/调试用的临时控制（相对摄像机，<see cref="Rigidbody.MovePosition"/>）。
    /// 鼠标左键拖动：屏幕左右/上下；W/S：沿摄像机前向前后。
    /// 按住 Alt 并移动鼠标：旋转 <see cref="referenceCamera"/> 朝向（水平绕世界 Y，垂直绕自身右轴）。
    /// 空格：爆炸；R：聚拢复原；T：清空目标槽 0 并释放该槽下已捕获碎片；A/D：模拟轨道左右滑动；Q：触发当前主面板 Enter。
    /// 需同物体上的 <see cref="EnergyBallController"/> 与 FX 组件。
    /// 目标需挂 <see cref="Rigidbody"/>（建议 Is Kinematic）。
    /// </summary>
    [DisallowMultipleComponent]
    [MovedFrom(false, null, null, "EnergyBallTempDragController")]
    public class EnergyBallDragController : MonoBehaviour
    {
        [Header("能量球")]
        [SerializeField] EnergyBallController controller;
        [SerializeField] EnergyBallPostExplosionPresentation postExplosionPresentation;

        [Header("移动目标")]
        [SerializeField] Transform targetTransform;
        [SerializeField] Rigidbody targetRigidbody;
        [SerializeField] Camera referenceCamera;

        [Header("灵敏度")]
        [SerializeField] float forwardSpeed = 2f;
        [SerializeField] float dragSensitivity = 0.01f;

        [Header("摄像机朝向（Alt + 鼠标）")]
        [SerializeField] float cameraLookSensitivity = 0.15f;
        [SerializeField] bool clampCameraPitch = true;
        [SerializeField] float minPitch = -80f;
        [SerializeField] float maxPitch = 80f;

        [Header("调试快捷键")]
        [SerializeField] bool enableDebugKeys = true;

        Vector2 _lastMousePosition;
        bool _isDragging;
        /// <summary>在 Update 中累积位移，FixedUpdate 中一次性施加，避免物理步长不一致。</summary>
        Vector3 _pendingDelta;

        void Awake()
        {
            if (controller == null)
                controller = GetComponent<EnergyBallController>();

            if (postExplosionPresentation == null)
                postExplosionPresentation = GetComponent<EnergyBallPostExplosionPresentation>();

            if (targetTransform == null)
                targetTransform = transform;

            if (targetRigidbody == null)
                targetRigidbody = targetTransform.GetComponent<Rigidbody>();

            if (referenceCamera == null)
                referenceCamera = Camera.main;
        }

        void Update()
        {
            if (enableDebugKeys)
                HandleDebugKeys();

            if (referenceCamera == null || targetRigidbody == null)
                return;

            Transform cam = referenceCamera.transform;
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            bool altLookActive = keyboard != null &&
                                 (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed);

            if (altLookActive && mouse != null)
                ApplyCameraLook(cam, mouse);

            // W/S：沿摄像机前向平移
            if (keyboard != null)
            {
                float forwardInput = 0f;
                if (keyboard.wKey.isPressed)
                    forwardInput += 1f;
                if (keyboard.sKey.isPressed)
                    forwardInput -= 1f;

                if (forwardInput != 0f)
                    _pendingDelta += cam.forward * (forwardInput * forwardSpeed * Time.deltaTime);
            }

            if (mouse == null || altLookActive)
                return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _isDragging = true;
                _lastMousePosition = mouse.position.ReadValue();
            }

            if (mouse.leftButton.wasReleasedThisFrame)
                _isDragging = false;

            if (!_isDragging || !mouse.leftButton.isPressed)
                return;

            // 左键拖动：映射到摄像机 right/up
            Vector2 current = mouse.position.ReadValue();
            Vector2 delta = current - _lastMousePosition;
            _lastMousePosition = current;

            _pendingDelta += cam.right * (delta.x * dragSensitivity) + cam.up * (delta.y * dragSensitivity);
        }

        void ApplyCameraLook(Transform cam, Mouse mouse)
        {
            if (cameraLookSensitivity <= 0f)
                return;

            Vector2 lookDelta = mouse.delta.ReadValue();
            if (lookDelta.sqrMagnitude <= 0f)
                return;

            cam.Rotate(Vector3.up, lookDelta.x * cameraLookSensitivity, Space.World);
            cam.Rotate(Vector3.right, -lookDelta.y * cameraLookSensitivity, Space.Self);

            if (!clampCameraPitch)
                return;

            Vector3 euler = cam.localEulerAngles;
            float pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            cam.localRotation = Quaternion.Euler(pitch, euler.y, euler.z);
        }

        void HandleDebugKeys()
        {
            if (controller == null && postExplosionPresentation == null)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (controller != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame)
                    controller.Explode();

                if (keyboard.rKey.wasPressedThisFrame)
                    controller.Regroup();

                if (keyboard.tKey.wasPressedThisFrame)
                    controller.ClearTargetSlot(0);
            }

            if (postExplosionPresentation != null)
            {
                if (keyboard.aKey.wasPressedThisFrame)
                    postExplosionPresentation.RequestSlideToDirection(-1);

                if (keyboard.dKey.wasPressedThisFrame)
                    postExplosionPresentation.RequestSlideToDirection(1);

                if (keyboard.qKey.wasPressedThisFrame)
                    postExplosionPresentation.TriggerCurrentMainPanel();
            }
        }

        void FixedUpdate()
        {
            if (targetRigidbody == null || _pendingDelta.sqrMagnitude <= 0f)
                return;

            targetRigidbody.MovePosition(targetRigidbody.position + _pendingDelta);
            _pendingDelta = Vector3.zero;
        }
    }
}
