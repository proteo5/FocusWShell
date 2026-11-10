# ADR 0004 — .NET 10 + C# + CsWin32 + WPF

- **Estado:** Aceptada
- **Fecha:** 2026-11-09

## Contexto

El gestor necesita: acceso amplio a Win32 y DWM, un bucle de mensajes para los hooks, ventanas propias sin activación y con transparencia para los overlays, un icono de bandeja, y un núcleo de dominio fácil de probar. Es un proyecto personal y open source, así que importan la velocidad de desarrollo y la facilidad para contribuir.

Opciones consideradas:

| Opción | A favor | En contra |
| --- | --- | --- |
| **C# / .NET 10 + WPF** | Productivo; WPF es maduro para ventanas sin borde, transparentes y `WS_EX_NOACTIVATE`; pruebas unitarias sencillas; publicación como ejecutable único. | Arranque y memoria mayores que en nativo; WPF tiene su propio manejo de DPI que hay que configurar bien. |
| C# / .NET + WinUI 3 | Aspecto moderno de Windows 11. | Empaquetado y despliegue más complicados; menos control sobre estilos de ventana; menos maduro para overlays. |
| C++ / Win32 directo | Control total, mínimo consumo. | Mucho más lento de desarrollar y de probar; más difícil para contribuir. |
| Rust (`windows-rs`) | Seguro y eficiente; komorebi demuestra que es viable. | UI propia más costosa; curva más alta para este proyecto. |

## Decisión

- **.NET 10 (LTS)** y C#.
- **[CsWin32](https://github.com/microsoft/CsWin32)** para generar las declaraciones P/Invoke desde los metadatos oficiales, en lugar de escribirlas a mano.
- **WPF** para la bandeja, los overlays y la configuración.
- Tres proyectos con dependencias en un solo sentido: `Core` (sin Windows) ← `Win32` ← `App`. Ver [03 — Arquitectura](../03-arquitectura.md#estructura-del-repositorio-propuesta).
- Manifiesto de aplicación con **PerMonitorV2** y `asInvoker` (nunca `requireAdministrator`).
- Pruebas con xUnit.

## Consecuencias

- El núcleo se prueba sin abrir ventanas reales.
- Hay que configurar con cuidado el DPI de WPF para que sus coordenadas coincidan con las del orquestador (píxeles físicos).
- Si M0 demuestra que el consumo o la latencia no son aceptables, se puede reescribir sólo la capa `Win32` sin tocar el dominio.
