# Документация GalacticExpansion (GLEX)

**Назначение**: единый вход в документацию проекта **GLEX** (server-side DLL мод для Empyrion: Galactic Survival).  
**Аудитория**: разработчики (первый приоритет).  
**Статус**: draft (создано из черновика, будет уточняться по мере реализации кода).  

## С чего начать разработчику

1) Прочитайте обзор и термины:
- [`overview/01_what_is_glex.md`](overview/01_what_is_glex.md)
- [`overview/02_glossary.md`](overview/02_glossary.md)

2) Подготовьте окружение и разберитесь с циклом ModAPI (events/requests):
- [`getting-started/01_dev_prerequisites.md`](getting-started/01_dev_prerequisites.md)
- [`getting-started/02_build_and_run.md`](getting-started/02_build_and_run.md)
- [`getting-started/03_debugging_and_logs.md`](getting-started/03_debugging_and_logs.md)

3) Изучите архитектуру и модули:
- [`architecture/02_Архитектурный_план.md`](architecture/02_Архитектурный_план.md)
- [`architecture/00_Modular_Development_Plan.md`](architecture/00_Modular_Development_Plan.md)
- [`architecture/modules/README.md`](architecture/modules/README.md)

## Разделы

- **Обзор**
  - [`overview/01_what_is_glex.md`](overview/01_what_is_glex.md)
  - [`overview/02_glossary.md`](overview/02_glossary.md)

- **Getting Started**
  - [`getting-started/01_dev_prerequisites.md`](getting-started/01_dev_prerequisites.md)
  - [`getting-started/02_build_and_run.md`](getting-started/02_build_and_run.md)
  - [`getting-started/03_debugging_and_logs.md`](getting-started/03_debugging_and_logs.md)
  - [`getting-started/04_examples_links.md`](getting-started/04_examples_links.md)

- **Архитектура**
  - [`architecture/Roadmap.md`](architecture/Roadmap.md)
  - [`architecture/00_Modular_Development_Plan.md`](architecture/00_Modular_Development_Plan.md)
  - [`architecture/02_Архитектурный_план.md`](architecture/02_Архитектурный_план.md)
  - [`architecture/03_Технический_проект.md`](architecture/03_Технический_проект.md)
  - [`architecture/04_data_model.md`](architecture/04_data_model.md)
  - [`architecture/05_placement_and_spawn.md`](architecture/05_placement_and_spawn.md)
  - [`architecture/06_security_model.md`](architecture/06_security_model.md)

- **Дизайн и механики**
  - [`design/01_gameplay_simulation.md`](design/01_gameplay_simulation.md)
  - [`design/02_stages_and_prefabs.md`](design/02_stages_and_prefabs.md)
  - [`design/03_threat_director.md`](design/03_threat_director.md)

- **Конфигурация и состояние**
  - [`config/ConfigReference.md`](config/ConfigReference.md)
  - [`config/state_schema.md`](config/state_schema.md)
  - `config/config_examples/` (примеры)

- **Эксплуатация**
  - [`ops/Operations_Runbook.md`](ops/Operations_Runbook.md)
  - [`ops/Troubleshooting.md`](ops/Troubleshooting.md)

- **Тестирование**
  - [`testing/09_Testing_Strategy.md`](testing/09_Testing_Strategy.md)

- **ADR (Architectural Decision Records)**
  - `adr/` (решения, которые “прибиваются гвоздями” по мере реализации)

- **Вклад в проект**
  - [`contributing/CONTRIBUTING.md`](contributing/CONTRIBUTING.md)
  - [`contributing/DOCS_STYLE_GUIDE.md`](contributing/DOCS_STYLE_GUIDE.md)

## Примечания

- Черновик-источник (будет “нарезан” на документы выше): [`00_наброски проекта.md`](00_наброски%20проекта.md)
- Папка `docs/examples/` содержит код/README сторонних модов как **референсы** (не исходники GLEX).
