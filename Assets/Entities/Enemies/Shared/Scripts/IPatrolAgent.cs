namespace Enemies
{
	// Враг, которому можно назначить маршрут патруля извне — например, точкой спавна (EnemySpawnPoint):
	// маршрут принадлежит уровню, а префаб врага один на все маршруты.
	public interface IPatrolAgent
	{
		void SetPatrolRoute(PatrolRoute route);
	}
}
