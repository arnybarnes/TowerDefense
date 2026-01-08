using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif
using UnityEngine.UI;

namespace TowerDefense
{
    public class GameController : MonoBehaviour
    {
        public static GameController Instance { get; private set; }

        private readonly List<Enemy> _enemies = new List<Enemy>();
        private readonly List<Tower> _towers = new List<Tower>();
        private readonly List<BuildSpot> _buildSpots = new List<BuildSpot>();
        private readonly List<Vector3> _path = new List<Vector3>();

        private readonly TowerDesign[] _towerCatalog =
        {
            new TowerDesign(
                "INF",
                "Infantry turret",
                new[]
                {
                    new TowerTier(125, 8f, 1.1f, 16f, 0f, 28f, new Color(0.2f, 0.75f, 0.9f)),
                    new TowerTier(175, 9.5f, 1.25f, 22f, 0f, 32f, new Color(0.15f, 0.9f, 0.65f))
                },
                "Balanced fire rate and solid early-game coverage."),
            new TowerDesign(
                "ART",
                "Artillery drone",
                new[]
                {
                    new TowerTier(160, 10f, 0.55f, 36f, 2.5f, 20f, new Color(0.95f, 0.55f, 0.25f)),
                    new TowerTier(220, 11.5f, 0.7f, 48f, 3.5f, 24f, new Color(1f, 0.75f, 0.3f))
                },
                "Long-range splash that excels versus clustered or armored targets.")
        };

        private readonly WaveDefinition[] _waves =
        {
            new WaveDefinition(
                "Recon Patrol",
                new EnemyArchetype("Light scouts", 60f, 3.2f, 6, 1, 0.75f, new Color(0.85f, 0.95f, 1f)),
                10,
                0.85f),
            new WaveDefinition(
                "Skirmishers",
                new EnemyArchetype("Fast bikes", 50f, 4.2f, 7, 1, 0.4f, new Color(0.95f, 0.65f, 0.3f)),
                14,
                0.7f),
            new WaveDefinition(
                "Armored Push",
                new EnemyArchetype("APC", 160f, 2.6f, 14, 2, 3.5f, new Color(0.4f, 0.7f, 0.35f)),
                12,
                1f),
            new WaveDefinition(
                "Mixed Assault",
                new EnemyArchetype("Combined arms", 120f, 3.2f, 12, 2, 2.2f, new Color(0.8f, 0.7f, 0.95f)),
                16,
                0.8f),
            new WaveDefinition(
                "Boss Convoy",
                new EnemyArchetype("Siege tank", 380f, 2.35f, 35, 4, 4f, new Color(0.85f, 0.15f, 0.15f), true),
                6,
                1.2f)
        };

        private int _selectedTowerIndex;
        private int _currency = 260;
        private int _baseHealth = 20;
        private int _currentWaveIndex = -1;
        private bool _waveInProgress;
        private float _lastWaveClearTime;
        private const float WaveRestSeconds = 6f;

        private Transform _environmentRoot;
        private Transform _baseCore;
        private Material _pathMaterial;
        private Material _buildSpotMaterial;
        private Material _enemyMaterial;
        private Material _towerMaterial;

        private Canvas _canvas;
        private Text _currencyText;
        private Text _waveText;
        private Text _baseText;
        private Text _statusText;
        private Text _selectionText;
        private Text _instructionsText;
        private Button _nextWaveButton;
        private Image _hudBackdrop;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            ConfigureCamera();
            EnsureEventSystem();

            _environmentRoot = new GameObject("Environment").transform;
            _environmentRoot.SetParent(transform, false);
            BuildMaterials();
            BuildPathAndMap();
            BuildUI();
            UpdateUI();
            Status("Place towers, then start the first wave.");
        }

        private void Update()
        {
            if (_baseHealth <= 0)
            {
                return;
            }

            bool pressed1 = false;
            bool pressed2 = false;
            bool pressedTab = false;

#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                pressed1 = keyboard.digit1Key.wasPressedThisFrame;
                pressed2 = keyboard.digit2Key.wasPressedThisFrame;
                pressedTab = keyboard.tabKey.wasPressedThisFrame;
            }
#endif

            if (pressed1)
            {
                SelectTower(0);
            }

            if (pressed2)
            {
                SelectTower(1);
            }

            if (pressedTab)
            {
                SelectTower((_selectedTowerIndex + 1) % _towerCatalog.Length);
            }

            if (!_waveInProgress && _currentWaveIndex >= 0 && Time.time - _lastWaveClearTime > WaveRestSeconds)
            {
                StartNextWave();
            }
        }

        public IReadOnlyList<Enemy> ActiveEnemies => _enemies;

        public bool TryBuildTower(BuildSpot spot)
        {
            if (_baseHealth <= 0)
            {
                return false;
            }

            var design = _towerCatalog[_selectedTowerIndex];
            var tier = design.Tiers[0];
            if (_currency < tier.Cost)
            {
                Status("Not enough currency to deploy that turret.");
                return false;
            }

            _currency -= tier.Cost;
            var tower = Tower.Create(design, spot, _towerMaterial, _towerMaterial);
            _towers.Add(tower);
            UpdateUI();
            Status($"{tower.DisplayName} deployed.");
            return true;
        }

        public bool TryUpgradeTower(Tower tower)
        {
            if (!tower.HasNextTier)
            {
                Status("Tower is maxed.");
                return false;
            }

            int cost = tower.NextTierCost;
            if (_currency < cost)
            {
                Status("Need more currency for the upgrade.");
                return false;
            }

            _currency -= cost;
            tower.Upgrade();
            UpdateUI();
            Status($"{tower.DisplayName} upgraded.");
            return true;
        }

        public void RegisterEnemy(Enemy enemy)
        {
            _enemies.Add(enemy);
            UpdateUI();
        }

        public void DeregisterEnemy(Enemy enemy)
        {
            _enemies.Remove(enemy);
            UpdateUI();
        }

        public void EnemyKilled(int reward)
        {
            _currency += reward;
            UpdateUI();
        }

        public void EnemyReachedBase(int damage)
        {
            _baseHealth = Mathf.Max(0, _baseHealth - damage);
            UpdateUI();
            if (_baseHealth <= 0)
            {
                Status("Base destroyed. Press Play again to restart.");
                _nextWaveButton.interactable = false;
            }
            else
            {
                Status($"Base under attack! {_baseHealth} HP remaining.");
            }
        }

        public void StartNextWave()
        {
            if (_waveInProgress || _baseHealth <= 0)
            {
                return;
            }

            _currentWaveIndex++;
            var wave = GetWaveForIndex(_currentWaveIndex);
            StartCoroutine(RunWave(wave));
        }

        private IEnumerator RunWave(WaveDefinition wave)
        {
            _waveInProgress = true;
            _nextWaveButton.interactable = false;
            Status($"Wave {_currentWaveIndex + 1}: {wave.Name} ({wave.Archetype.Label})");
            UpdateUI();

            for (int i = 0; i < wave.Count; i++)
            {
                SpawnEnemy(wave.Archetype);
                yield return new WaitForSeconds(wave.SpawnInterval);
            }

            while (_enemies.Count > 0)
            {
                yield return null;
            }

            _waveInProgress = false;
            _lastWaveClearTime = Time.time;
            Status($"Wave cleared. Build or upgrade before the next attack.");
            _nextWaveButton.interactable = true;
        }

        private void SpawnEnemy(EnemyArchetype archetype)
        {
            var enemy = Enemy.Create(archetype, _path, _enemyMaterial);
            enemy.transform.SetParent(_environmentRoot, false);
        }

        private WaveDefinition GetWaveForIndex(int index)
        {
            if (index < _waves.Length)
            {
                return _waves[index];
            }

            var template = _waves[_waves.Length - 1];
            int additional = index - _waves.Length + 1;
            return template.CreateScaled($"Endless {_currentWaveIndex + 1}", 1f + additional * 0.35f);
        }

        private void SelectTower(int index)
        {
            _selectedTowerIndex = Mathf.Clamp(index, 0, _towerCatalog.Length - 1);
            UpdateUI();
        }

        private void ConfigureCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var camGo = new GameObject("Main Camera");
                camera = camGo.AddComponent<Camera>();
                camGo.tag = "MainCamera";
            }

            camera.orthographic = true;
            camera.orthographicSize = 18f;
            camera.transform.position = new Vector3(0, 35f, 0);
            camera.transform.rotation = Quaternion.Euler(90f, 0, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.1f, 0.12f);
        }

        private void EnsureEventSystem()
        {
#if ENABLE_INPUT_SYSTEM
            var existing = Object.FindObjectOfType<EventSystem>();
            if (existing != null)
            {
                var legacy = existing.GetComponent<StandaloneInputModule>();
                if (legacy != null)
                {
                    Destroy(legacy);
                }

                if (existing.GetComponent<InputSystemUIInputModule>() == null)
                {
                    existing.gameObject.AddComponent<InputSystemUIInputModule>();
                }
                return;
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(eventSystem);
#else
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(eventSystem);
#endif
        }

        private void BuildMaterials()
        {
            _pathMaterial = CreateLitMaterial(new Color(0.18f, 0.23f, 0.32f));
            _buildSpotMaterial = CreateLitMaterial(new Color(0.3f, 0.42f, 0.52f));
            _enemyMaterial = CreateLitMaterial(new Color(0.9f, 0.3f, 0.3f));
            _towerMaterial = CreateLitMaterial(new Color(0.9f, 0.9f, 0.95f));
        }

        private void BuildPathAndMap()
        {
            CreateGround();

            _path.Clear();
            _path.AddRange(new[]
            {
                new Vector3(-20f, 0f, -10f),
                new Vector3(-10f, 0f, -10f),
                new Vector3(-10f, 0f, 4f),
                new Vector3(2f, 0f, 4f),
                new Vector3(2f, 0f, -6f),
                new Vector3(16f, 0f, -6f)
            });

            var pathRoot = new GameObject("Path").transform;
            pathRoot.SetParent(_environmentRoot, false);
            for (int i = 0; i < _path.Count - 1; i++)
            {
                var start = _path[i];
                var end = _path[i + 1];
                var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"Path_{i}";
                segment.transform.SetParent(pathRoot, false);
                var mid = (start + end) * 0.5f;
                var dir = (end - start);
                var length = dir.magnitude;
                var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
                segment.transform.SetPositionAndRotation(mid + Vector3.down * 0.4f, look);
                segment.transform.localScale = new Vector3(2.5f, 0.2f, length + 1f);
                var renderer = segment.GetComponent<Renderer>();
                renderer.sharedMaterial = _pathMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var collider = segment.GetComponent<Collider>();
                Destroy(collider);
            }

            var beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "SpawnBeacon";
            beacon.transform.SetParent(pathRoot, false);
            beacon.transform.position = _path[0] + Vector3.up * 0.7f;
            beacon.transform.localScale = new Vector3(0.8f, 0.25f, 0.8f);
            beacon.GetComponent<Renderer>().sharedMaterial = CreateLitMaterial(new Color(0.2f, 0.8f, 0.9f));

            _baseCore = GameObject.CreatePrimitive(PrimitiveType.Cylinder).transform;
            _baseCore.name = "BaseCore";
            _baseCore.SetParent(pathRoot, false);
            _baseCore.transform.position = _path[_path.Count - 1] + Vector3.up * 0.5f;
            _baseCore.transform.localScale = new Vector3(2.2f, 0.5f, 2.2f);
            _baseCore.GetComponent<Renderer>().sharedMaterial = CreateLitMaterial(new Color(0.9f, 0.9f, 0.4f));

            CreateBuildSpots();
        }

        private void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(_environmentRoot, false);
            ground.transform.localScale = new Vector3(5f, 1f, 5f);
            var renderer = ground.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateLitMaterial(new Color(0.08f, 0.15f, 0.18f));
        }

        private void CreateBuildSpots()
        {
            var pads = new[]
            {
                new Vector3(-16f, 0f, -4f),
                new Vector3(-16f, 0f, -14f),
                new Vector3(-8f, 0f, -2f),
                new Vector3(-2f, 0f, 8f),
                new Vector3(6f, 0f, 0f),
                new Vector3(10f, 0f, -12f),
                new Vector3(14f, 0f, -2f)
            };

            var padRoot = new GameObject("BuildSpots").transform;
            padRoot.SetParent(_environmentRoot, false);
            foreach (var pos in pads)
            {
                var pad = BuildSpot.Create(this, pos, _buildSpotMaterial);
                pad.transform.SetParent(padRoot, false);
                _buildSpots.Add(pad);
            }
        }

        private Material CreateLitMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader)
            {
                color = color
            };
            material.enableInstancing = true;
            return material;
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // LegacyRuntime.ttf is the built-in replacement for Arial in newer Unity versions.
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }

            _hudBackdrop = new GameObject("Backdrop", typeof(Image)).GetComponent<Image>();
            _hudBackdrop.transform.SetParent(canvasGo.transform, false);
            var rect = _hudBackdrop.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _hudBackdrop.color = new Color(0f, 0f, 0f, 0.35f);
            ApplyBackgroundTexture(_hudBackdrop);

            var topBar = CreatePanel("TopBar", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -10f), new Vector2(0f, 110f), new Color(0.1f, 0.13f, 0.18f, 0.75f));

            _currencyText = CreateLabel("Currency", topBar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, -20f), font, 20);
            _waveText = CreateLabel("Wave", topBar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, -50f), font, 20);
            _baseText = CreateLabel("Base", topBar, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(20f, -80f), font, 20);

            _statusText = CreateLabel("Status", topBar, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -35f), font, 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            _statusText.rectTransform.sizeDelta = new Vector2(500f, 50f);

            _nextWaveButton = CreateButton("StartWaveButton", topBar, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-170f, -40f), new Vector2(150f, 60f), font, "Start Wave");
            _nextWaveButton.onClick.AddListener(StartNextWave);

            _selectionText = CreateLabel("Selection", canvasGo.transform, new Vector2(0.02f, 0f), new Vector2(0.02f, 0f), new Vector2(10f, 18f), font, 18, TextAnchor.UpperLeft, new Vector2(0f, 0f));
            _selectionText.rectTransform.sizeDelta = new Vector2(420f, 80f);

            _instructionsText = CreateLabel(
                "Instructions",
                canvasGo.transform,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 18f),
                font,
                16,
                TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0f));
            _instructionsText.rectTransform.sizeDelta = new Vector2(920f, 60f);
            _instructionsText.text = "Click pads to build. Click towers to upgrade. [1]/[2]/[Tab] select tower types. Use Start Wave to deploy the next attack.";
        }

        private RectTransform CreatePanel(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var panel = new GameObject(name, typeof(Image)).GetComponent<Image>();
            panel.transform.SetParent(_canvas.transform, false);
            panel.color = color;
            var rect = panel.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private Text CreateLabel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offset, Font font, int size, TextAnchor anchor = TextAnchor.UpperLeft, Vector2? pivot = null)
        {
            var label = new GameObject(name, typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(parent, false);
            var rect = label.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot ?? new Vector2(0f, 1f);
            rect.anchoredPosition = offset;
            label.font = font;
            label.fontSize = size;
            label.color = new Color(0.9f, 0.95f, 1f);
            label.alignment = anchor;
            return label;
        }

        private Button CreateButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size, Font font, string text)
        {
            var buttonGo = new GameObject(name, typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(parent, false);
            var rect = buttonGo.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = buttonGo.GetComponent<Image>();
            image.color = new Color(0.2f, 0.6f, 0.9f, 0.9f);
            var button = buttonGo.GetComponent<Button>();
            var label = CreateLabel("Label", buttonGo.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, font, 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f));
            label.rectTransform.sizeDelta = size;
            label.text = text;
            return button;
        }

        private void ApplyBackgroundTexture(Image target)
        {
            var texture = Resources.Load<Texture2D>("Art/OIP-4029944637") ?? Resources.Load<Texture2D>("Art/600x372_warzone-tower-defense");
            if (texture == null)
            {
                return;
            }

            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            target.sprite = sprite;
            target.color = new Color(1f, 1f, 1f, 0.2f);
            target.type = Image.Type.Simple;
            target.preserveAspect = true;
        }

        private void Status(string message)
        {
            _statusText.text = message;
        }

        private void UpdateUI()
        {
            var design = _towerCatalog[_selectedTowerIndex];
            _currencyText.text = $"Currency: {_currency}";
            var waveLabel = _currentWaveIndex < 0 ? "Ready" : (_currentWaveIndex + 1).ToString();
            _waveText.text = $"Wave: {waveLabel} | Enemies: {_enemies.Count}";
            _baseText.text = $"Base Health: {_baseHealth}";
            _selectionText.text =
                $"Selected: {design.DisplayName} | Cost {design.Tiers[0].Cost} | Upgrade {design.NextTierCost(0)} | Range {design.Tiers[0].Range}";
            _nextWaveButton.interactable = !_waveInProgress && _baseHealth > 0;
        }
    }

    public readonly struct TowerDesign
    {
        public string Key { get; }
        public string DisplayName { get; }
        public TowerTier[] Tiers { get; }
        public string Flavor { get; }

        public TowerDesign(string key, string displayName, TowerTier[] tiers, string flavor)
        {
            Key = key;
            DisplayName = displayName;
            Tiers = tiers;
            Flavor = flavor;
        }

        public int NextTierCost(int currentTier)
        {
            var nextIndex = currentTier + 1;
            return nextIndex < Tiers.Length ? Tiers[nextIndex].Cost : 0;
        }
    }

    public readonly struct TowerTier
    {
        public int Cost { get; }
        public float Range { get; }
        public float FireRate { get; }
        public float Damage { get; }
        public float SplashRadius { get; }
        public float ProjectileSpeed { get; }
        public Color Color { get; }

        public TowerTier(int cost, float range, float fireRate, float damage, float splashRadius, float projectileSpeed, Color color)
        {
            Cost = cost;
            Range = range;
            FireRate = fireRate;
            Damage = damage;
            SplashRadius = splashRadius;
            ProjectileSpeed = projectileSpeed;
            Color = color;
        }
    }

    internal readonly struct WaveDefinition
    {
        public string Name { get; }
        public EnemyArchetype Archetype { get; }
        public int Count { get; }
        public float SpawnInterval { get; }

        public WaveDefinition(string name, EnemyArchetype archetype, int count, float spawnInterval)
        {
            Name = name;
            Archetype = archetype;
            Count = count;
            SpawnInterval = spawnInterval;
        }

        public WaveDefinition CreateScaled(string name, float factor)
        {
            var scaled = Archetype.Scaled(factor);
            var scaledCount = Mathf.CeilToInt(Count * Mathf.Lerp(1f, factor, 0.75f));
            return new WaveDefinition(name, scaled, scaledCount, Mathf.Max(0.35f, SpawnInterval * Mathf.Lerp(1f, 1f / factor, 0.35f)));
        }
    }
}
