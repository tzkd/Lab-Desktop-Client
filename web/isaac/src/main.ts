import './style.css';
import {
  AppStreamer,
  DirectConfig,
  EventAction,
  EventStatus,
  LogLevel,
  StreamEvent,
  StreamProps,
  StreamType,
} from '@nvidia/ov-web-rtc';

type SessionConfiguration = {
  type: 'connect';
  signalingPort: number;
  turnHost: string;
  turnPort: number;
  turnUserName: string;
  turnCredential: string;
  width: number;
  height: number;
};

declare global {
  interface Window {
    chrome?: {
      webview?: {
        addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
        postMessage(message: unknown): void;
      };
    };
  }
}

const videoContainer = requiredElement('video-container');
const message = requiredElement('message');
const bridge = window.chrome?.webview;
let streamer: AppStreamer | undefined;
let started = false;
let closing = false;

function requiredElement(id: string): HTMLElement {
  const element = document.getElementById(id);
  if (!element) {
    throw new Error(`Missing element: ${id}`);
  }
  return element;
}

function report(state: 'ready' | 'progress' | 'connected' | 'error' | 'diagnostic', detail = ''): void {
  bridge?.postMessage({ type: 'status', state, detail, secureContext: window.isSecureContext });
}

function installRtcDiagnostics(): void {
  const NativePeerConnection = window.RTCPeerConnection;
  class DiagnosedPeerConnection extends NativePeerConnection {
    constructor(configuration?: RTCConfiguration) {
      super(configuration);
      const servers = (configuration?.iceServers || []).flatMap(server => {
        const urls = typeof server.urls === 'string' ? [server.urls] : server.urls;
        return urls.map(url => {
          try {
            const parsed = new URL(url.replace(/^turn:/, 'http:').replace(/^turns:/, 'https:'));
            return `${url.startsWith('turns:') ? 'turns' : 'turn'}:${parsed.hostname}:${parsed.port || '-'}:${url.includes('transport=tcp') ? 'tcp' : 'default'}`;
          } catch {
            return 'invalid-turn-uri';
          }
        });
      });
      report(
        'diagnostic',
        `rtc-config policy=${configuration?.iceTransportPolicy || 'all'} servers=${servers.join(',') || 'none'}`,
      );
      this.addEventListener('icecandidate', event => {
        const candidateType = event.candidate?.type || (event.candidate ? 'unknown' : 'complete');
        report('diagnostic', `ice-candidate type=${candidateType}`);
      });
      this.addEventListener('icecandidateerror', event => {
        report(
          'diagnostic',
          `ice-error code=${event.errorCode} text=${event.errorText || '-'} host=${event.address || '-'} port=${event.port || '-'}`,
        );
      });
      this.addEventListener('icegatheringstatechange', () => {
        report('diagnostic', `ice-gathering state=${this.iceGatheringState}`);
      });
    }
  }
  window.RTCPeerConnection = DiagnosedPeerConnection;
}

function showError(detail: string): void {
  videoContainer.hidden = true;
  message.hidden = false;
  message.textContent = detail || 'Isaac Sim GUI 连接失败。';
  report('error', message.textContent);
}

function isConfiguration(value: unknown): value is SessionConfiguration {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Record<string, unknown>;
  return candidate.type === 'connect' &&
    Number.isInteger(candidate.signalingPort) &&
    typeof candidate.turnHost === 'string' && /^\d{1,3}(?:\.\d{1,3}){3}$/.test(candidate.turnHost) &&
    Number.isInteger(candidate.turnPort) &&
    typeof candidate.turnUserName === 'string' && candidate.turnUserName.length > 0 &&
    typeof candidate.turnCredential === 'string' && candidate.turnCredential.length > 0 &&
    Number.isInteger(candidate.width) &&
    Number.isInteger(candidate.height);
}

async function connect(configuration: SessionConfiguration): Promise<void> {
  if (started) return;
  started = true;
  installRtcDiagnostics();
  const onStart = (event: StreamEvent): void => {
    if (event.action !== EventAction.START) return;
    if (event.status === EventStatus.SUCCESS) {
      message.hidden = true;
      videoContainer.hidden = false;
      videoContainer.focus();
      report('connected');
    } else if (event.status === EventStatus.ERROR || event.status === EventStatus.CANCELED) {
      showError(String(event.info || 'Isaac Sim GUI 连接失败。'));
    } else {
      const detail = String(event.info || '正在建立媒体连接…');
      message.textContent = detail;
      report('progress', detail);
    }
  };
  const streamConfig: DirectConfig = {
    videoElementId: 'remote-video',
    audioElementId: 'remote-audio',
    signalingServer: '127.0.0.1',
    signalingPort: configuration.signalingPort,
    width: configuration.width,
    height: configuration.height,
    fps: 30,
    // Keep the retry budget below the native host's 60-second startup bound so
    // the SDK can return its final, actionable transport error to the user.
    maxReconnects: 1,
    reconnectDelay: 2000,
    connectivityTimeout: 15000,
    fitStreamResolution: true,
    iceServerConfiguration: {
      iceServers: [{
        urls: `turn:${configuration.turnHost}:${configuration.turnPort}?transport=tcp`,
        username: configuration.turnUserName,
        credential: configuration.turnCredential,
      }],
      iceTransportPolicy: 'relay',
    },
    onStart,
    onStop: (event: StreamEvent): void => {
      if (!closing) {
        showError(String(event.info || 'Isaac Sim GUI 连接已停止。'));
      }
    },
  };
  const streamProps: StreamProps = {
    streamSource: StreamType.DIRECT,
    logLevel: LogLevel.INFO,
    streamConfig,
  };
  streamer = new AppStreamer();
  try {
    await streamer.connect(streamProps);
  } catch (error) {
    showError(error instanceof Error ? error.message : String(error));
  }
}

bridge?.addEventListener('message', (event: MessageEvent): void => {
  if (!isConfiguration(event.data)) {
    showError('客户端传入了无效的会话参数。');
    return;
  }
  void connect(event.data);
});

window.addEventListener('pagehide', (): void => {
  closing = true;
  if (streamer) {
    void streamer.terminate(false);
  }
});

if (window.isSecureContext) {
  report('ready');
} else {
  showError('客户端页面未运行在安全上下文中。');
}
