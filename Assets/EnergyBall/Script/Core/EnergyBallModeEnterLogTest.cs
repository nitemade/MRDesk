using UnityEngine;

namespace EnergyBall
{
    /// <summary>IEnergyBallMode 的测试实现：仅在 Enter 输出物体名称。</summary>
    public class EnergyBallModeEnterLogTest : MonoBehaviour, IEnergyBallMode
    {
        public string ModeName => nameof(EnergyBallModeEnterLogTest);

        public void Enter()
        {
            Debug.Log(gameObject.name);
        }

        public void Exit()
        {
        }

        public void Tick()
        {
        }

        public void Trigger()
        {
        }
    }
}
