# OpenCamInterop integration

OpenCamInterop is an experimental standalone interoperability project consumed by IPCamLapse from the `OpenCamInterop/` source tree. It gives camera, NVR, and automation projects a small common event envelope while keeping each source format's unavoidable differences visible.

Its EventLab workflow inspects a problematic event, verifies a sanitized corpus, replays it deterministically in CI, and turns a hardware-specific quirk into an executable compatibility test. It does not rename or replace IPCamLapse.

## What is implemented

- A packable .NET 10 `OpenCamInterop` library using the official CloudEvents C# JSON formatter
- A pure Frigate `events` transformer for `new`, `update`, and `end` object messages
- A pure ONVIF transformer for WS-Notification `Notify` and ONVIF `PullMessagesResponse` payloads
- Versioned JSON Schemas under `OpenCamInterop/schemas/v1`
- Synthetic fixtures under `OpenCamInterop/fixtures/v1`
- An offline .NET 10 EventLab with `inspect`, strict manifest `verify`, and streaming NDJSON `replay`
- A generated fixture behavior matrix checked on Windows and Ubuntu
- A read-only IPCamLapse activity export at `GET /api/sessions/{id}/events/cloudevents?limit=50`

All transformers operate on caller-supplied bytes. There is no MQTT client, SOAP subscription client, camera discovery, video handling, credential storage, or dynamic plug-in loading in this slice.

## Event contract

Every output is a CloudEvents 1.0 event with an absolute `source`, required UTC `time`, `application/json` data, and a versioned `type` and `dataschema`.

| Input | CloudEvent type | Data schema |
|---|---|---|
| Frigate `new` | `com.kalyterasystems.opencaminterop.object.detected.v1` | `urn:opencaminterop:schema:camera-object-event:1` |
| Frigate `update` | `com.kalyterasystems.opencaminterop.object.updated.v1` | `urn:opencaminterop:schema:camera-object-event:1` |
| Frigate `end` | `com.kalyterasystems.opencaminterop.object.ended.v1` | `urn:opencaminterop:schema:camera-object-event:1` |
| Canonical ONVIF motion `Changed` | `com.kalyterasystems.opencaminterop.signal.changed.v1` | `urn:opencaminterop:schema:camera-signal-event:1` |
| Other ONVIF notification | `com.kalyterasystems.opencaminterop.onvif.notification.v1` | `urn:opencaminterop:schema:onvif-notification-event:1` |

CloudEvent identity is deterministic. A byte-for-byte Frigate redelivery on the same topic receives the same ID. ONVIF IDs hash one normalized notification—topic, dialect, time, operation, and ordered items—so SOAP wrapper formatting or batch position does not change the ID. Consumers must use `source` plus `id` as the uniqueness pair.

Frigate occurrence time is `end_time` for `end`, otherwise `frame_time` when present and `start_time` as a deterministic fallback. `start_time` and `end_time` remain in event data. ONVIF requires a valid, explicitly zoned `UtcTime`; the adapter does not silently replace a malformed occurrence time.

Only an ONVIF motion topic in the standard ONVIF topic namespace and a recognized Concrete or ConcreteSet dialect is promoted to `signal.changed.v1`. `Initialized` and `Deleted` property operations remain generic notifications, so a synchronization snapshot cannot be mistaken for a fresh motion trigger.

## Privacy and trust boundary

The adapters are designed for untrusted payloads, but fixture authors still own the final sanitization decision.

- Adapter payloads are limited to 1 MiB; XML DTDs and external resolution are prohibited; XML depth, notification counts, and item counts are bounded.
- Frigate output is constructed from an allowlist. Fields such as sub-labels, recognized plates, thumbnails, and arbitrary native extensions are not copied.
- Canonical ONVIF motion output contains a Boolean state plus stable opaque camera and optional rule identifiers. Raw ONVIF tokens do not leave the adapter.
- Generic ONVIF events retain item names and ordering, including duplicate names observed on nonconforming devices, but replace every item value with `[redacted]`. Nonstandard topic namespaces are pseudonymized.
- IPCamLapse export omits camera URLs, credentials, storage paths, and raw diagnostic messages. Frame paths are reduced to a basename and failures to a small code vocabulary.
- Structured JSON decoding rejects duplicate object members, oversized input, and oversized batches. For recognized OpenCamInterop v1 types it also rejects invalid typed data and a mismatched schema; the published schema files are contracts for consumers, not a general runtime schema registry.

The caller-configured CloudEvent `source` is emitted verbatim. Frigate's allowed camera IDs, object IDs, labels, and zone names are also retained because consumers need them for correlation. Use an opaque URN for `source` and synthetic camera, object, label, and zone identifiers in shared fixtures. Allowlisting and opaque SHA-256 identifiers are not automatic anonymity; hashes are neither encryption nor protection against guessing a low-entropy token. Do not submit real secrets or personal data even when a field would normally be hashed or redacted.

## Adding a compatibility fixture

A worthwhile fixture represents a behavior that was not already covered. Useful examples include unusual namespace prefixes, synchronization ordering, malformed timestamps, missing sections, duplicate fields, reconnect duplicates, and safe handling of a vendor extension.

1. Reproduce the behavior with a minimal synthetic payload. Do not paste a raw camera export.
2. Submit it to the [standalone OpenCamInterop repository](https://github.com/KalyteraSystems/OpenCamInterop) beneath `fixtures/v1/{adapter}`.
3. Register the case in `fixtures/v1/manifest.json` with an expected event sequence or diagnostic.
4. Explain the interoperability gap and the information deliberately removed during sanitization.
5. Run the standalone verifier and full checks from its `CONTRIBUTING.md`.

A model number, compatibility-table row, or mechanically split fixture is not enough by itself. One focused contribution should describe one distinct behavior and its executable expectation.

## Non-goals and honest status

This is not an ONVIF conformance suite, certification, complete semantic ontology, NVR, viewer, camera-control system, or production network bridge. ONVIF and vendor names identify input formats; they do not imply endorsement. The fixtures are synthetic and do not claim physical-device coverage yet.

The standalone split is based on an executable contribution surface: a strict manifest, an offline inspect/verify/replay CLI, generic tests, and independent CI. IPCamLapse remains the first-party source consumer. The current standalone evidence is still only four synthetic cases across three payloads and two input families, with zero externally derived cases and zero independent consumers; the split itself is not counted as adoption.

## Protocol references

- [CloudEvents specification and JSON format](https://github.com/cloudevents/spec)
- [Official CloudEvents C# SDK](https://github.com/cloudevents/sdk-csharp)
- [Frigate MQTT event documentation](https://docs.frigate.video/integrations/mqtt/)
- [ONVIF network interface specifications](https://www.onvif.org/profiles/specifications/)
- [OASIS WS-Topics 1.3](https://docs.oasis-open.org/wsn/wsn-ws_topics-1.3-spec-os.htm)
