using UnityEngine;
using UnityEngine.EventSystems;

namespace TowerDefense
{
    public class Tower : MonoBehaviour
    {
        private TowerDesign _design;
        private int _tierIndex;
        private float _cooldown;
        private BuildSpot _spot;
        private Transform _pivot;
        private Transform _muzzle;
        private Renderer _baseRenderer;
        private Renderer _headRenderer;

        public bool HasNextTier => _tierIndex + 1 < _design.Tiers.Length;
        public int NextTierCost => HasNextTier ? _design.Tiers[_tierIndex + 1].Cost : 0;
        public string DisplayName => $"{_design.DisplayName} T{_tierIndex + 1}";

        private TowerTier CurrentTier => _design.Tiers[_tierIndex];

        public static Tower Create(TowerDesign design, BuildSpot spot, Material baseMaterial, Material headMaterial)
        {
            var go = new GameObject($"{design.Key}_Tower");
            var tower = go.AddComponent<Tower>();
            tower.Initialize(design, spot, baseMaterial, headMaterial);
            return tower;
        }

        private void Initialize(TowerDesign design, BuildSpot spot, Material baseMaterial, Material headMaterial)
        {
            _design = design;
            _spot = spot;
            _tierIndex = 0;

            transform.SetParent(spot.transform, false);
            transform.position = spot.transform.position + Vector3.up * 0.4f;

            var baseMesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseMesh.name = "Base";
            baseMesh.transform.SetParent(transform, false);
            baseMesh.transform.localScale = new Vector3(1.2f, 0.3f, 1.2f);
            _baseRenderer = baseMesh.GetComponent<Renderer>();
            _baseRenderer.sharedMaterial = new Material(baseMaterial);

            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(transform, false);
            _pivot.localPosition = new Vector3(0f, 0.6f, 0f);

            var head = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            head.name = "Turret";
            head.transform.SetParent(_pivot, false);
            head.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            _headRenderer = head.GetComponent<Renderer>();
            _headRenderer.sharedMaterial = new Material(headMaterial);

            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_pivot, false);
            _muzzle.localPosition = new Vector3(0f, 0.3f, 0.5f);

            var clickCollider = gameObject.AddComponent<SphereCollider>();
            clickCollider.radius = 1.4f;
            clickCollider.isTrigger = true;

            var baseCollider = baseMesh.GetComponent<Collider>();
            baseCollider.enabled = false;

            _spot.AttachTower(this);
            ApplyTierVisual();
        }

        private void Update()
        {
            if (GameController.Instance == null || GameController.Instance.ActiveEnemies.Count == 0)
            {
                return;
            }

            var target = FindTarget();
            if (target == null)
            {
                return;
            }

            AimAt(target);
            if (_cooldown > 0f)
            {
                _cooldown -= Time.deltaTime;
                return;
            }

            Fire(target);
        }

        private Enemy FindTarget()
        {
            var enemies = GameController.Instance.ActiveEnemies;
            Enemy closest = null;
            var range = CurrentTier.Range;
            var sqrRange = range * range;
            foreach (var enemy in enemies)
            {
                var sqr = (enemy.transform.position - transform.position).sqrMagnitude;
                if (sqr <= sqrRange && (closest == null || sqr < (closest.transform.position - transform.position).sqrMagnitude))
                {
                    closest = enemy;
                }
            }

            return closest;
        }

        private void AimAt(Enemy target)
        {
            var lookPos = target.transform.position;
            lookPos.y = _pivot.position.y;
            _pivot.LookAt(lookPos, Vector3.up);
        }

        private void Fire(Enemy target)
        {
            var tier = CurrentTier;
            Projectile.Create(_muzzle.position, target, tier.Damage, tier.SplashRadius, tier.ProjectileSpeed, tier.Color);
            _cooldown = 1f / Mathf.Max(0.01f, tier.FireRate);
        }

        public void Upgrade()
        {
            if (!HasNextTier)
            {
                return;
            }

            _tierIndex++;
            ApplyTierVisual();
        }

        private void ApplyTierVisual()
        {
            var tier = CurrentTier;
            _baseRenderer.sharedMaterial.color = tier.Color * 0.7f;
            _headRenderer.sharedMaterial.color = tier.Color;
            transform.localScale = Vector3.one * (1f + _tierIndex * 0.05f);
        }

        private void OnMouseDown()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            GameController.Instance?.TryUpgradeTower(this);
        }
    }
}
