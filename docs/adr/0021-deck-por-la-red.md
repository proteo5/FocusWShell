# ADR 0021 — El deck por la red, como app web servida por FocusWShell

- **Estado:** Aceptada (2026-11-27): transporte por HTTP (opción A) tras el spike S13; MVP hecho.
- **Fecha:** 2026-11-27

## Contexto

Hoy el deck es una ventana de WPF ([ADR 0013](0013-deck-superficie-de-control.md)). Para usarlo en una tablet hace falta Apollo, que crea un monitor virtual, y Artemis, que lo recibe por streaming. Funciona (spike S8), pero cuesta:

- **Instalar y configurar** Apollo y Artemis, y un monitor virtual que Windows trata como uno más.
- **Video continuo:** batería en la tablet, red y algo de latencia.
- **La tablet deja de ser tablet** mientras hace de deck.
- **Sólo funciona donde hay cliente de Moonlight** que se haya probado (hoy, la tablet Android del usuario).

El ADR 0013 ya preveía una fase 3, el deck como página web por la red local, y una «API de deck» para que esa fase no obligara a reescribir el núcleo. Esa API existe:

- **El estado:** `DeckState`, con el foco, los paneles con sus apps, Libres y los monitores. `PageWindow` lleva las páginas de los navegadores.
- **Los comandos:** los `Command` del controlador (`Promote`, `Adopt`, `Release`, `Park`, `SetPresentation`, `MakeActiveInPanel`…) más las acciones de `DeckActions`: abrir una app, cerrar, traer al frente, anclar, energía, pantalla encendida, ratón al foco y páginas.

## Decisión (propuesta)

### Qué es

- **FocusWShell sirve el deck como app web** en la red local. La tablet o el teléfono la abren en el navegador y la dejan en la pantalla de inicio. Sirve igual en Android y en iPad, sin tiendas.
- **La página viaja con FocusWShell:** la versión del deck siempre coincide con la del PC.
- **No reemplaza al deck de WPF.** El de la esquina del principal y el de Apollo siguen. Los tres usan la misma API, y una acción en uno se ve al momento en los otros.
- **Apagado por defecto.** Se prende en la ventana principal, en una sección **Deck en la red**.

### Arquitectura

```
Tablet (navegador)                          PC (FocusWShell)
┌───────────────────────┐   WebSocket   ┌───────────────────────────────┐
│ Deck web (HTML/JS)    │◄─────────────►│ Servidor local (Kestrel)       │
│ dibuja el estado      │   estado →    │  ├─ estado: DeckState → JSON   │
│ manda comandos        │   ← comandos  │  ├─ comandos → controlador     │
└───────────────────────┘               │  └─ iconos: /icono/{app}.png  │
                                        └───────────────────────────────┘
```

- **Servidor:** Kestrel, dentro del proceso de FocusWShell. No necesita permisos de administrador para escuchar, a diferencia de `HttpListener`, que pide reservar la URL.
- **Estado:** un JSON derivado de `DeckState`. Se manda entero al conectarse y cada vez que cambia (hoy el deck de WPF ya calcula una firma para no redibujar sin cambios). Son unos pocos KB.
- **Comandos:** mensajes JSON con un tipo y sus datos (`{"cmd":"promote","window":"w-123"}`). Entran por el mismo camino que los toques del deck de WPF, así que valen las mismas reglas (ADR 0016, bandeja, elevadas).
- **Identificadores:** las ventanas viajan con un id opaco que el servidor traduce a su `HWND`. Nunca se aceptan handles crudos del cliente.
- **Iconos:** PNG por app, pedidos una vez y guardados en la caché del navegador.
- **Páginas de los navegadores:** se leen al pedirlas, como hoy. Los títulos no se guardan, y las ventanas privadas siguen sin títulos (ADR 0019, enmienda).

### Emparejamiento y permisos

- **Emparejar con un QR.** La sección Deck en la red muestra un QR con la dirección del PC y un código de un solo uso. La tablet lo escanea y recibe un **token propio**, guardado en el navegador.
- **Cada dispositivo se ve y se revoca** desde la ventana principal, con su nombre y la última vez que se conectó.
- **Sin token, nada.** El servidor sólo responde la página de emparejamiento a quien no está emparejado.
- **Sólo redes privadas.** No escucha si la red de Windows está marcada como pública, y rechaza conexiones que no vengan de la red local.
- **Energía (apagar, reiniciar, cerrar sesión) viene desactivada** para los dispositivos remotos; se prende por dispositivo.
- **El Firewall de Windows** pide permiso la primera vez que FocusWShell escucha en la red. El spike mide si un usuario sin permisos de administrador puede darlo.

### Transporte seguro: lo decide el spike S13

Un navegador sólo da lo que el deck necesita (pantalla completa como app instalada, mantener la pantalla encendida con *Wake Lock*, *service worker*) en un **contexto seguro**: HTTPS con un certificado que confía. En una red local no hay un certificado público. Las opciones:

| Opción | Cómo | A favor | En contra |
| --- | --- | --- | --- |
| **A. HTTP en la red local** | Sin cifrado; el token autentica cada mensaje | Lo más simple; funciona en cualquier navegador | Sin contexto seguro: sin *Wake Lock* ni instalación real como app. Los títulos de las páginas viajan sin cifrar por la red |
| **B. HTTPS con certificado propio** | FocusWShell crea su certificado; la tablet instala su raíz una vez | Contexto seguro, todo cifrado | Instalar un certificado raíz en Android y en iPad es incómodo y da miedo |
| **C. App mínima nativa** | Una app que sólo contiene un WebView con el deck y fija la huella del certificado (del QR) | Cifrado, pantalla encendida y pantalla completa sin pasos raros | Hay que distribuirla: APK en Android (fácil), TestFlight o App Store en iPad (cuenta de Apple, Mac) |

**Recomendación previa:** empezar con **A** para medir y validar el uso, con los títulos de Páginas apagados por defecto en remoto, y pasar a **C** si el deck web se queda. La app web sería la misma en las dos; C sólo la envuelve.

### Promesa de privacidad

Hoy General dice «Funciona sólo en tu PC: no se conecta a la red». Con el deck en la red apagado, sigue siendo cierto. Al prenderlo, el texto cambia a: no se conecta a internet; sólo escucha en tu red local para los dispositivos que emparejaste.

## Spike S13

Antes de construir el deck web completo:

1. **Servidor mínimo:** Kestrel en FocusWShell, una página que muestre los paneles con sus iconos y un toque que lleve una app al foco.
2. **Latencia** del toque al cambio en el PC, por wifi, en la tablet Android y en un iPad.
3. **Firewall:** qué pide Windows la primera vez, y si se puede aceptar sin ser administrador.
4. **Pantalla encendida y pantalla completa** con la opción A en Android (Chrome) y en iPad (Safari): qué se consigue sin contexto seguro.
5. **Tamaño:** cuánto crece el zip autocontenido con Kestrel.
6. **Reconexión:** la tablet que se duerme y vuelve, el PC que cambia de IP.

Con eso se elige el transporte (A, B o C) y se cierra este ADR.

## Plan

1. Spike S13 y decisión de transporte.
2. **MVP del deck web:** el foco, los paneles de todos los monitores (como el ADR 0020), Libres, mover y Páginas. Emparejar y revocar.
3. El resto del deck: modos (anclar, minimizar, cerrar), disposición del principal, sistema y energía.
4. Si se elige C: la app mínima para Android y, después, para iPad.

## Consecuencias

- **La tablet ya no necesita Apollo para el deck.** Apollo sigue sirviendo si la quieres como monitor con paneles.
- **La interfaz del deck se escribe dos veces:** WPF y web. Para no duplicar las reglas, las decisiones (qué se puede tocar en cada modo, qué es destino al mover) deberían moverse del `DeckWindow` a un modelo compartido. Se hace poco a poco, empezando por lo que el MVP necesite.
- **FocusWShell pasa a escuchar en la red** cuando se prende. Es la primera vez; por eso va apagado, emparejado, revocable y sólo en redes privadas.
- **Un punto más que mantener:** el protocolo. Lleva versión desde el primer mensaje, para que un deck viejo en caché no mande comandos que el PC ya no entiende.

## Resultado del spike S13 (2026-11-27)

Medido en la tablet Android por wifi (detalle en [riesgos y spikes](../04-riesgos-y-spikes.md#s13--deck-por-la-red-2026-11-27)):

- **Latencia:** 190–280 ms del toque a la app en el foco, de los que 160–206 ms son del PC (llevar al foco y activar) y 15–74 ms de la red. La red no es el problema; el cambio de foco sí, y es el mismo con el deck de Apollo.
- **La app queda al frente con el teclado** aunque el toque venga de la red.
- **Firewall:** sólo pidió confirmación, sin administrador.
- **Por HTTP:** pantalla completa sí; contexto seguro y Wake Lock no.
- **Reconexión:** sola, tras bloquear la tablet.
- **Tamaño:** +11.4 MB en el zip.

**Decisión propuesta sobre el transporte:**

- **El MVP va por HTTP (opción A)**, con el emparejamiento por QR y los títulos de Páginas apagados por defecto en remoto, porque viajan sin cifrar.
- **La pantalla encendida, por ahora en la tablet:** su propio tiempo de pantalla, o una alternativa sin contexto seguro que se mide en el MVP.
- **Si el deck web se queda, la opción C** (una app mínima con WebView para Android, y después para iPad) agrega cifrado con el certificado fijado y Wake Lock, sin cambiar la página.
- **Falta probar en un iPad.**

## MVP (2026-11-27)

Hecho y probado en la tablet Android del usuario:

- **Servidor:** `DeckWebServer` (Kestrel) en el puerto 8765, sólo para direcciones privadas. Protocolo v1 por WebSocket: el estado cuando cambia (cada medio segundo como mucho) y los comandos `tap`, `move`, `previous`, `toPanel`, `freeAll`, `pages`, `selectPage` y `closePage`. Las apps y las páginas viajan con ids opacos.
- **Acciones compartidas:** el deck de la ventana y el de la red usan las mismas `DeckActions`. Las reglas de un toque (`DeckWindow.Choose`) están copiadas en `DeckWebServer.Tap`; se unifican cuando el deck web crezca.
- **Emparejamiento:** sección **Deck en la red** con un QR y un código de 6 cifras de un solo uso que caduca en 5 minutos. Cada dispositivo guarda su token en una cookie; en el PC se guarda sólo su hash (`deck-web.json`). La dirección que queda tras emparejar lleva el token, para que una app de la pantalla de inicio del iPad (que no comparte cookies con Safari) entre directo. **Quitar** corta sus conexiones al momento.
- **Páginas:** apagadas por defecto; se prenden en la sección, con el aviso de que viajan sin cifrar.
- **Interfaz:** dos columnas en horizontal (incluida una tablet pequeña), una en vertical o en un teléfono; todos los monitores apilados (ADR 0020); ⛶ entra y sale de pantalla completa; las apps sin icono muestran su inicial.
- **Privacidad:** el texto de General cambia cuando el deck en la red está prendido.

Pendiente: los modos Anclar, Minimizar y Cerrar, la disposición del principal, la energía (desactivada en remoto), la pantalla encendida (opción C), comprobar la categoría de red de Windows (hoy sólo se mira que la dirección sea privada) y probar en un iPad.

## Seguridad (2026-11-28)

Antes de publicarlo en una alfa:

1. **Emparejar con límite.** Cinco intentos fallidos anulan el código (`DeckWebDevices.MaxFailures`), y cada dirección que falla cinco veces espera 10 minutos; cada intento tarda 300 ms.
2. **Origen y host.** El `Host` tiene que ser una dirección del PC en la que escucha (contra el DNS rebinding), y lo que cambia algo (emparejar, el WebSocket) exige un `Origin` igual al de la página. Cabeceras `nosniff` y `X-Frame-Options: DENY`.
3. **Sólo redes privadas o de dominio de Windows** (`PrivateNetworks`, con el Network List Manager). Kestrel escucha sólo en las direcciones de esas redes, nunca en una pública ni en adaptadores que Windows no reconoce (Hyper-V, Docker). Se vuelve a elegir cuando cambian las direcciones y cada 20 s, porque marcar una red como privada no avisa. Si todas las redes son públicas, la sección lo explica y lleva a los ajustes de red de Windows. Hallazgo: el wifi del usuario estaba marcado como público, como hace Windows con las redes nuevas.
4. **Límites.** Tres conexiones por dispositivo, 32 en total, peticiones de 4 KB como mucho, y leer las páginas como mucho cada 2 s por conexión.

Hallazgo del Firewall: la primera vez que FocusWShell escucha, Windows pregunta y crea la regla para el perfil de la red de ese momento. Con el wifi marcado como público, la regla quedó sólo para redes públicas; al marcarlo como privado, el Firewall bloqueaba la tablet y no volvía a preguntar. Se arregla a mano (con permisos de administrador); la sección y el manual lo explican.

Sigue pendiente el cifrado (opción C): sin él, quien comparta la red puede ver el tráfico, incluido el token.
