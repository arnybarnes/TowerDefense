using UnityEngine;

namespace TowerDefense
{
    public class Projectile : MonoBehaviour
    {
        private Enemy _target;
        private float _damage;
        private float _splashRadius;
        private float _speed;
        private float _lifeTimer = 5f;
        private Vector3 _lastKnownTarget;

        public static Projectile Create(Vector3 origin, Enemy target, float damage, float splashRadius, float speed, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Projectile";
            var projectile = go.AddComponent<Projectile>();
            projectile.Initialize(origin, target, damage, splashRadius, speed, color);
            return projectile;
        }

        private void Initialize(Vector3 origin, Enemy target, float damage, float splashRadius, float speed, Color color)
        {
            _target = target;
            _damage = damage;
            _splashRadius = splashRadius;
            _speed = speed;
            _lastKnownTarget = target != null ? target.transform.position : origin;
            transform.position = origin;
            transform.localScale = Vector3.one * 0.35f;

            var renderer = GetComponent<Renderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            renderer.sharedMaterial = new Material(shader)
            {
                color = color
            };

            var collider = GetComponent<Collider>();
            Destroy(collider);
        }

        private void Update()
        {
            _lifeTimer -= Time.deltaTime;
            if (_lifeTimer <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            if (_target != null)
            {
                _lastKnownTarget = _target.transform.position;
            }

            var destination = _lastKnownTarget + Vector3.up * 0.2f;
            var step = _speed * Time.deltaTime;
            transform.position = Vector3.MoveTowards(transform.position, destination, step);

            if (Vector3.Distance(transform.position, destination) < 0.15f)
            {
                Impact();
            }
        }

        private void Impact()
        {
            if (_target != null)
            {
                _target.TakeDamage(_damage);
            }

            if (_splashRadius > 0.01f && GameController.Instance != null)
            {
                var enemies = GameController.Instance.ActiveEnemies;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var enemy = enemies[i];
                    if (enemy == null || enemy == _target)
                    {
                        continue;
                    }

                    if (Vector3.Distance(enemy.transform.position, transform.position) <= _splashRadius)
                    {
                        enemy.TakeDamage(_damage * 0.65f);
                    }
                }
            }

            Destroy(gameObject);
        }
    }
}
