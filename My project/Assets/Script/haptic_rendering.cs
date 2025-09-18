using System.Collections;
using UnityEngine;
using System.IO.Ports;
using System.Threading;

public class HapticRendering : MonoBehaviour
{
    // --- Serial / Encoder ---
    [Header("Serial")]
    [SerializeField] private string comPort = "COM12";
    [SerializeField] private int baudRate = 115200;

    private SerialPort serialPort;
    private Thread readThread;
    private volatile bool isRunning;

    private long encoderLeft;
    private long encoderRight;

    // --- Calibration ---
    private long baseEncLeft = 0;
    private long baseEncRight = 0;
    private bool calibrated = false;

    // U I O P 수동 미세 이동
    [Header("Manual Nudge (encoder counts)")]
    [SerializeField] private int nudgeCounts = 100;
    private bool fromUiOp = false;

    // --- Handshake ---
    [Header("Handshake")]
    [Tooltip("mm -> encoder counts 변환 계수")]
    public float ENCODER_PER_MM = 90.8f;

    [Tooltip("Handshake 1회 길이 (고정: 4.0s)")]
    public float handshakeDuration = 4.0f;

    [Tooltip("H 키를 눌러 Handshake 1회 실행")]
    public KeyCode handshakeKey = KeyCode.H;

    private Coroutine handshakeCo;
    private bool stopping = false; // X 키로 토글

    public HandshakeManager hsmanager;

    // ▲ 추가: HandShake_on 엣지 감지를 위한 이전 상태 저장
    private bool prevHandshakeOn = false;

    // ---------------- Serial Read (Left/Right encoders) ----------------
    void ReadSerial()
    {
        while (isRunning)
        {
            try
            {
                if (serialPort != null && serialPort.IsOpen && serialPort.BytesToRead >= 12)
                {
                    int startByte = serialPort.ReadByte();
                    if (startByte == 0xAA)
                    {
                        byte[] leftBytes = new byte[4];
                        byte[] rightBytes = new byte[4];
                        byte[] fsrBytes = new byte[2]; // 받더라도 사용하지 않음

                        serialPort.Read(leftBytes, 0, 4);
                        serialPort.Read(rightBytes, 0, 4);
                        serialPort.Read(fsrBytes, 0, 2);

                        int endByte = serialPort.ReadByte();
                        if (endByte == 0x55)
                        {
                            long leftVal = System.BitConverter.ToInt32(leftBytes, 0);
                            long rightVal = System.BitConverter.ToInt32(rightBytes, 0);
                            lock (this)
                            {
                                encoderLeft = leftVal;
                                encoderRight = rightVal;
                            }
                        }
                    }
                }
            }
            catch { /* 타임아웃 등 무시 */ }
        }
    }

    // ---------------- Commands ----------------
    private void SendCommand(char cmd, long t1, long t2)
    {
        // 자동 전송은 캘리브레이션 반영, 수동(U/I/O/P)은 반영 X
        if (calibrated && !fromUiOp)
        {
            t1 += baseEncLeft;
            t2 += baseEncRight;
        }
        string msg = $"{cmd},{t1},{t2}\n";
        if (serialPort != null && serialPort.IsOpen)
        {
            serialPort.Write(msg);
        }
        fromUiOp = false; // 원복
    }

    private void SendStopCommand()
    {
        if (serialPort != null && serialPort.IsOpen)
        {
            serialPort.Write("b\n");
        }
    }

    // ---------------- Unity Lifecycle ----------------
    void Start()
    {
        try
        {
            serialPort = new SerialPort(comPort, baudRate);
            serialPort.ReadTimeout = 100;
            serialPort.WriteTimeout = 100;
            serialPort.Open();
            Debug.Log("Serial Opened: " + comPort);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Serial Open Failed: " + e.Message);
        }

        isRunning = true;
        readThread = new Thread(ReadSerial);
        readThread.Start();
    }

    void Update()
    {
        // --- 수동 미세 이동 (U/I/O/P) ---
        if (Input.GetKeyDown(KeyCode.U))
        {
            long t1 = encoderLeft + nudgeCounts;
            long t2 = encoderRight;
            fromUiOp = true;
            SendCommand('a', t1, t2);
        }
        if (Input.GetKeyDown(KeyCode.I))
        {
            long t1 = encoderLeft - nudgeCounts;
            long t2 = encoderRight;
            fromUiOp = true;
            SendCommand('a', t1, t2);
        }
        if (Input.GetKeyDown(KeyCode.O))
        {
            long t1 = encoderLeft;
            long t2 = encoderRight + nudgeCounts;
            fromUiOp = true;
            SendCommand('a', t1, t2);
        }
        if (Input.GetKeyDown(KeyCode.P))
        {
            long t1 = encoderLeft;
            long t2 = encoderRight - nudgeCounts;
            fromUiOp = true;
            SendCommand('a', t1, t2);
        }

        // --- X: 정지 토글 ---
        if (Input.GetKeyDown(KeyCode.X))
        {
            stopping = !stopping;
            SendStopCommand();
            Debug.Log("Stopping = " + stopping);
        }

        // --- Z: 캘리브레이션 ---
        if (Input.GetKeyDown(KeyCode.Z))
        {
            lock (this)
            {
                baseEncLeft = encoderLeft;
                baseEncRight = encoderRight;
            }
            calibrated = true;
            Debug.Log($"Calibration Done. baseLeft={baseEncLeft}, baseRight={baseEncRight}");
        }

        // --- H: 수동 Handshake 1회 실행 (디버그용) ---
        if (Input.GetKeyDown(handshakeKey))
        {
            StartHandshakeOnce();
        }

        // --- HandShake_on 엣지 기반 제어 ---
        if (hsmanager != null)
        {
            bool hs = hsmanager.HandShake_on;

            // false -> true : 시작 (한 번만)
            if (hs && !prevHandshakeOn)
            {
                StartHandshakeOnce();
            }
            // true -> false : 정지 (한 번만)
            else if (!hs && prevHandshakeOn)
            {
                StopHandshakeIfRunning();
            }

            prevHandshakeOn = hs;
        }
    }

    private void StartHandshakeOnce()
    {
        if (handshakeCo != null) StopCoroutine(handshakeCo);
        handshakeCo = StartCoroutine(HandshakeRoutine());
    }

    private void StopHandshakeIfRunning()
    {
        if (handshakeCo != null)
        {
            StopCoroutine(handshakeCo);
            handshakeCo = null;
        }
        SendStopCommand();
    }

    private void OnApplicationQuit()
    {
        isRunning = false;
        if (readThread != null && readThread.IsAlive) readThread.Join();

        if (serialPort != null && serialPort.IsOpen) serialPort.Close();
    }

    // ---------------- Handshake Logic (4s) ----------------
    IEnumerator HandshakeRoutine()
    {
        float T = handshakeDuration; // 4.0s
        if (T <= 0f) yield break;

        float t = 0f;
        while (t < T)
        {
            // 외부에서 정지 토글되면 즉시 멈춤
            if (stopping)
            {
                SendStopCommand();
                yield return null;
                t += Time.deltaTime;
                continue;
            }

            // ★ Animator가 Idle로 돌아가면(= HandShake_on false) 바로 종료
            if (hsmanager != null && !hsmanager.HandShake_on)
            {
                break;
            }

            // --- y(t) cm ---
            float y;
            if (t <= 0.5f)
            {
                float u = t / 0.5f;      // 0 -> 1
                y = Mathf.Lerp(0f, 8f, u);
            }
            else if (t <= 3.5f)
            {
                y = 8f;
            }
            else
            {
                float u = (t - 3.5f) / 0.5f; // 0 -> 1
                y = Mathf.Lerp(8f, 0f, u);
            }

            // --- x(t) cm ---
            float x;
            if (t <= 1.0f)
            {
                float u = t / 1.0f;
                x = Mathf.Lerp(3f, 4f, u);
            }
            else if (t <= 2.0f)
            {
                float u = (t - 1.0f) / 1.0f;
                x = Mathf.Lerp(4f, 3f, u);
            }
            else
            {
                float u = (t - 2.0f) / 2.0f;
                x = Mathf.Lerp(3f, 5f, u);
            }

            // --- fingerTotal in [0..1] ---
            float fingerTotal = (y <= 0f) ? 0f : (y < 1f ? y : 1f);

            // --- ratio from x (3~5 cm) ---
            float ratio = (x <= 3f) ? 0f : (x >= 5f ? 1f : (x - 3f) / 2f);

            // --- mm → enc ---
            float A_mm = fingerTotal * (1f - ratio) * 5f;
            float B_mm = fingerTotal * ratio * 5f;

            long A_enc = (long)(A_mm * ENCODER_PER_MM);
            long B_enc = (long)(B_mm * ENCODER_PER_MM);

            SendCommand('a', A_enc, B_enc);

            yield return null;
            t += Time.deltaTime;
        }

        // 종료 시 안전 정지
        SendStopCommand();
        handshakeCo = null;
    }
}
