# Glosario

La documentación está en español y el código en inglés. Esta tabla fija la correspondencia para que no haya dos nombres para lo mismo.

| Término | En código | Significado |
| --- | --- | --- |
| Centro (de atención) | `Center`, `CenterRegion` | Región central protegida donde vive la tarea principal. Aloja como máximo una ventana. |
| Trabajo | `Work` | Hogar de las herramientas activas de apoyo. |
| Contexto | `Context` | Hogar de la información periférica: correo, chat, calendario, estados. |
| Hogar | `Home` | Panel al que pertenece una ventana (`Work` o `Context`). Persistente. |
| Ubicación | `Placement` | Dónde está la ventana ahora: `InPanel`, `Center`, `Parked`, `Fullscreen`. |
| Estacionada | `Parked` | Abierta pero apartada (minimizada); se recupera desde la tira de su panel. |
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
