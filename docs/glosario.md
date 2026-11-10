# Glosario

La documentación está en español y el código en inglés. Esta tabla fija la correspondencia para que no haya dos nombres para lo mismo.

| Término | En código | Significado |
| --- | --- | --- |
| Centro (de atención) | `Center`, `CenterRegion` | Región central protegida donde vive la tarea principal. Aloja como máximo una ventana. |
| Trabajo | `Work` | Hogar de las herramientas activas de apoyo. |
| Contexto | `Context` | Hogar de la información periférica: correo, chat, calendario, estados. |
| Hogar | `Home` | Panel al que pertenece una ventana (`Work` o `Context`). Persistente. |
| Ubicación | `Placement` | Dónde está la ventana ahora: `InPanel`, `Center`, `Parked`, `Fullscreen`. |
| Estacionada | `Parked` | Abierta pero apartada (minimizada); se recupera desde la tira de su panel. Minimizarla con el botón de Windows la estaciona. |
| Estacionar / Recuperar | `Park` / `Restore` | Apartar una ventana sin quitarle su panel, y devolverla a su puesto. En la UI, "Recuperar" para no confundirlo con "Restaurar todas". |
| Fuera de layout | `Detached` | La ventana que el usuario arrastró, redimensionó o maximizó en el principal. El gestor no la mueve hasta que se reencaje. |
| Reencajar | `Reattach` | Devolver al layout una ventana que estaba fuera. |
| Fill | `PanelTemplate.Fill` | Template de un panel de un monitor auxiliar: la primera ventana ocupa el monitor, maximizada. |
| Pantalla completa | `Fullscreen` | Inmersión explícita en un monitor elegido. Distinta del Centro y del botón Maximizar de Windows. |
| Promover al foco | `Promote` | Llevar una ventana al Centro. La anterior vuelve a su hogar. No se llama "Maximizar". |
| Devolver | `ReturnToPanel` | Sacar la ventana del Centro y devolverla a su hogar. |
| Panel | `PanelRegion` | Región lateral con tipo (`Work`/`Context`) y template. |
| Template | `PanelTemplate` | Forma de presentar las ventanas de un panel: `Tiles`, `Stack`, `Collapsible`, `Mixed`. |
| Pila / pestañas | `Stack` | Template con una ventana visible y las demás en una tira de pestañas. |
| Escena | `Scene` | Configuración completa de una actividad: asignaciones, layouts y templates. |
| Layout de escena | `SceneLayout` | Geometría de una escena para una configuración de monitores concreta. |
| Huella de monitores | `MonitorSetup.Fingerprint` | Identificador de una combinación de monitores conectados. |
| Estado original | `OriginalState` | Geometría y estado de una ventana antes de que el gestor la tocara. |
| Diario | `Journal` | Archivo en disco que permite restaurar todo tras una caída. |
| Plan de layout | `LayoutPlan` | Lista de operaciones que el núcleo pide aplicar a las ventanas. |
| Microbarra | `PanelBar` | Barra propia del gestor en cada panel: pestañas, estacionadas, indicadores. |
| Restaurar todas | `RestoreAll` | Salida de emergencia: devuelve todas las ventanas a su estado original. |
| Catálogo de apps | `AppCatalog` | Apps que FocusWShell ha visto alguna vez, con cómo reconocerlas y cómo abrirlas. Se llena solo, por detección ([ADR 0011](adr/0011-grupos-de-apps-y-catalogo.md)). |
| Grupo | `AppGroup` | Conjunto libre de apps con un tipo (lateral, monitor, compacto o deck) que dice dónde y cómo se presentan. Contexto y Trabajo son los grupos laterales por defecto. |
| Vista | `View` | Plantilla del escritorio completo: cuántas áreas de foco hay y qué grupos van a los lados ([ADR 0012](adr/0012-vistas-y-areas-de-foco.md)). |
| Área de foco | `FocusArea` | Hueco del monitor principal para la ventana en la que se trabaja. Una o dos según la vista; con una, es el Centro. |
| Deck | `Deck` | Superficie de control táctil y opcional: grupos de apps, vistas, estado y botones ([ADR 0013](adr/0013-deck-superficie-de-control.md)). |
