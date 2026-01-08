using UnityEngine;
using UnityEngine.EventSystems;

namespace TowerDefense
{
    public class BuildSpot : MonoBehaviour
    {
        private GameController _controller;
        private Tower _tower;
        private Renderer _renderer;
        private Color _baseColor;

        public bool HasTower => _tower != null;

        public static BuildSpot Create(GameController controller, Vector3 position, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = $"BuildSpot_{position.x:0}_{position.z:0}";
            go.transform.position = position + Vector3.up * 0.15f;
            go.transform.localScale = new Vector3(1.7f, 0.25f, 1.7f);
            var spot = go.AddComponent<BuildSpot>();
            spot.Initialize(controller, material);
            return spot;
        }

        private void Initialize(GameController controller, Material material)
        {
            _controller = controller;
            _renderer = GetComponent<Renderer>();
            _renderer.sharedMaterial = new Material(material);
            _baseColor = _renderer.sharedMaterial.color;

            var collider = GetComponent<Collider>();
            collider.isTrigger = false;
        }

        public void AttachTower(Tower tower)
        {
            _tower = tower;
        }

        private void OnMouseDown()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            if (HasTower)
            {
                _controller.TryUpgradeTower(_tower);
            }
            else
            {
                if (_controller.TryBuildTower(this))
                {
                    _renderer.sharedMaterial.color = _baseColor * 0.9f;
                }
            }
        }

        private void OnMouseEnter()
        {
            if (_renderer != null)
            {
                _renderer.sharedMaterial.color = _baseColor * 1.2f;
            }
        }

        private void OnMouseExit()
        {
            if (_renderer != null)
            {
                _renderer.sharedMaterial.color = HasTower ? _baseColor * 0.9f : _baseColor;
            }
        }
    }
}
