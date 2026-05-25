namespace EnergyBall
{
    public interface IEnergyBallMode
    {
        string ModeName { get; }

        void Enter();

        void Exit();

        void Tick();

        void Trigger();
    }
}
