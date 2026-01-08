using System.Collections.Generic;
using UnityEngine;

namespace TowerDefense
{
    public class Enemy : MonoBehaviour
    {
        private EnemyArchetype _archetype;
        private readonly List<Vector3> _path = new List<Vector3>();
        private int _pathIndex = 1;
        private float _health;
        private Renderer _renderer;
        private bool _despawning;

        public EnemyArchetype Archetype => _archetype;
        public float Health => _health;

        public static Enemy Create(EnemyArchetype archetype, IList<Vector3> path, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"Enemy_{archetype.Label}";
            var enemy = go.AddComponent<Enemy>();
            enemy.Initialize(archetype, path, material);
            return enemy;
        }

        private void Initialize(EnemyArchetype archetype, IList<Vector3> path, Material material)
        {
            _archetype = archetype;
            _health = archetype.Health;
            _path.Clear();
            _path.AddRange(path);
            _pathIndex = Mathf.Min(1, _path.Count - 1);

            _renderer = GetComponent<Renderer>();
            _renderer.sharedMaterial = new Material(material)
            {
                color = archetype.Color
            };
            transform.position = _path[0] + Vector3.up * 0.5f;
            transform.localScale = Vector3.one * (archetype.IsBoss ? 1.25f : 0.8f);

            var collider = GetComponent<Collider>();
            collider.isTrigger = true;

            GameController.Instance.RegisterEnemy(this);
        }

        private void Update()
        {
            if (_health <= 0f || _pathIndex >= _path.Count)
            {
                return;
            }

            var target = _path[_pathIndex];
            target.y = transform.position.y;
            var step = _archetype.Speed * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, target, step);

            if (Vector3.Distance(transform.position, target) < 0.05f)
            {
                _pathIndex++;
                if (_pathIndex >= _path.Count)
                {
                    ReachBase();
                }
            }
        }

        public void TakeDamage(float amount)
        {
            amount = Mathf.Max(1f, amount - _archetype.Armor);
            _health -= amount;
            if (_renderer != null)
            {
                var c = _renderer.sharedMaterial.color;
                _renderer.sharedMaterial.color = Color.Lerp(c, Color.white, 0.1f);
            }

            if (_health <= 0f)
            {
                Die();
            }
        }

        private void ReachBase()
        {
            GameController.Instance.EnemyReachedBase(_archetype.BaseDamage);
            Cleanup();
        }

        private void Die()
        {
            GameController.Instance.EnemyKilled(_archetype.Reward);
            Cleanup();
        }

        private void Cleanup()
        {
            if (_despawning)
            {
                return;
            }

            _despawning = true;
            GameController.Instance.DeregisterEnemy(this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (!_despawning && GameController.Instance != null)
            {
                GameController.Instance.DeregisterEnemy(this);
            }
        }
    }

    public readonly struct EnemyArchetype
    {
        public string Label { get; }
        public float Health { get; }
        public float Speed { get; }
        public int Reward { get; }
        public int BaseDamage { get; }
        public float Armor { get; }
        public Color Color { get; }
        public bool IsBoss { get; }

        public EnemyArchetype(string label, float health, float speed, int reward, int baseDamage, float armor, Color color, bool isBoss = false)
        {
            Label = label;
            Health = health;
            Speed = speed;
            Reward = reward;
            BaseDamage = baseDamage;
            Armor = armor;
            Color = color;
            IsBoss = isBoss;
        }

        public EnemyArchetype Scaled(float factor)
        {
            return new EnemyArchetype(
                Label,
                Health * factor,
                Speed * Mathf.Lerp(1f, 1.15f, Mathf.Clamp01(factor - 1f)),
                Mathf.RoundToInt(Reward * factor),
                Mathf.RoundToInt(BaseDamage * Mathf.Lerp(1f, factor, 0.6f)),
                Armor + (factor - 1f) * 0.5f,
                Color,
                IsBoss);
        }
    }
}
