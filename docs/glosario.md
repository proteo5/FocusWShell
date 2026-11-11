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
| Estacionar / Recuperar | `Park` / `Restore` | Apartar una ventana sin quitarle su panel, y devolverla a su puesto. En la UI, "Recuperar" para no confundirlo con "Reordenar todas". |
| Fuera de layout | `Detached` | La ventana que el usuario maximizó en el principal. El gestor no la mueve hasta que se des-maximice o se reencaje. (Arrastrarla ya no la deja fuera: vuelve a su sitio o cambia de panel.) |
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
| Liberar todas | `RestoreAll` | Salida de emergencia: devuelve todas las ventanas a su estado original y las deja libres. Antes se llamaba "Restaurar todas"; en inglés, "Free all". |
| Reordenar todas | `RearrangeAll` | Deshace el último "Liberar todas": vuelve a poner en sus paneles las ventanas que siguen abiertas. Un solo nivel. |
| Anclar | `Pinned` | Una app anclada sigue saliendo en su panel, apagada, cuando está cerrada. Las no ancladas sólo salen abiertas. |
| Catálogo de apps | `AppCatalog` | Apps que FocusWShell ha visto alguna vez, con cómo reconocerlas y cómo abrirlas. Se llena solo, por detección ([ADR 0011](adr/0011-paneles-y-catalogo.md)). |
| Panel | `Panel` | Entidad con nombre (renombrable), ubicación (columna del principal, monitor auxiliar o tablet) y presentación. "Contexto" y "Trabajo" son los dos paneles por defecto ([ADR 0011](adr/0011-paneles-y-catalogo.md)). |
| Presentación | `PanelPresentation` | Cómo muestra un panel sus ventanas: tiles, lista (tipo Explorador), pestañas, colapsables, pantalla completa en el panel, mixto o deck. |
| Libre | — | Ventana que FocusWShell no gestiona: no la mueve. Es el estado de partida de toda ventana. |
| Vista | `View` | Plantilla del monitor principal: dónde va el foco (izquierda, centro o derecha) y la proporción de cada columna ([ADR 0012](adr/0012-vistas-posicion-del-foco.md)). |
| Deck | `Deck` | Superficie de control táctil y opcional: paneles con sus apps, vistas, estado y botones ([ADR 0013](adr/0013-deck-superficie-de-control.md)). |
