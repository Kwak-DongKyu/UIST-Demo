using System.Collections;
using UnityEngine;
using System.IO.Ports;
using System.Threading;

public class HapticRendering2 : MonoBehaviour
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

    [Header("Manual Nudge (encoder counts)")]
    [SerializeField] private int nudgeCounts = 100;
    private bool fromUiOp = false;

    [Header("Handshake")]
    public float ENCODER_PER_MM = 90.8f;
    public float handshakeDuration = 4.0f;
    public KeyCode handshakeKey = KeyCode.H;

    private Coroutine handshakeCo;
    private bool stopping = false; // X key toggle

    // n명 지원: 현재 햅틱 오너
    private HandshakeAgent boundAgent;

    // --- Serial Read Thread ---
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
                        byte[] fsrBytes = new byte[2];
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
            catch { /* ignore timeouts */ }
        }
    }

    // --- Commands ---
    private void SendCommand(char cmd, long t1, long t2)
    {
        if (calibrated && !fromUiOp)
        {
            t1 += baseEncLeft;
            t2 += baseEncRight;
        }
        string msg = $"{cmd},{t1},{t2}\n";
        if (serialPort != null && serialPort.IsOpen)
            serialPort.Write(msg);
        fromUiOp = false;
    }

    private void SendStopCommand()
    {
        if (serialPort != null && serialPort.IsOpen)
            serialPort.Write("b\n");
    }

    // --- Lifecycle ---
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
        // 수동 U/I/O/P
        if (Input.GetKeyDown(KeyCode.U)) { fromUiOp = true; SendCommand('a', encoderLeft + nudgeCounts, encoderRight); }
        if (Input.GetKeyDown(KeyCode.I)) { fromUiOp = true; SendCommand('a', encoderLeft - nudgeCounts, encoderRight); }
        if (Input.GetKeyDown(KeyCode.O)) { fromUiOp = true; SendCommand('a', encoderLeft, encoderRight + nudgeCounts); }
        if (Input.GetKeyDown(KeyCode.P)) { fromUiOp = true; SendCommand('a', encoderLeft, encoderRight - nudgeCounts); }

        // 정지 토글
        if (Input.GetKeyDown(KeyCode.X)) { stopping = !stopping; SendStopCommand(); }

        // 캘리브레이션
        if (Input.GetKeyDown(KeyCode.Z))
        {
            lock (this) { baseEncLeft = encoderLeft; baseEncRight = encoderRight; }
            calibrated = true;
            Debug.Log($"Calibration Done. baseLeft={baseEncLeft}, baseRight={baseEncRight}");
        }

        // 디버그: H키 수동 시작
        if (Input.GetKeyDown(handshakeKey)) StartHandshakeOnce();

        // 바운드 에이전트가 Idle로 돌아가면 종료
        if (boundAgent && handshakeCo != null && !boundAgent.HandShake_on)
            StopHandshakeNow();
    }

    private void OnApplicationQuit()
    {
        isRunning = false;
        if (readThread != null && readThread.IsAlive) readThread.Join();
        if (serialPort != null && serialPort.IsOpen) serialPort.Close();
    }

    // --- Public API for Orchestrator ---
    public void BindAgent(HandshakeAgent agent)
    {
        boundAgent = agent;
    }

    public void StartHandshakeOnce()
    {
        if (handshakeCo != null) StopCoroutine(handshakeCo);
        handshakeCo = StartCoroutine(HandshakeRoutine());
    }

    public void StopHandshakeNow()
    {
        if (handshakeCo != null)
        {
            StopCoroutine(handshakeCo);
            handshakeCo = null;
        }
        SendStopCommand();
    }

    // --- Handshake motion logic (4s) ---
    IEnumerator HandshakeRoutine()
    {
        float T = handshakeDuration;
        if (T <= 0f) yield break;

        float t = 0f;
        while (t < T)
        {
            if (stopping) { SendStopCommand(); t += Time.deltaTime; yield return null; continue; }
            if (boundAgent && !boundAgent.HandShake_on) break;

            // y(t) cm
            float y = (t <= 0.5f) ? Mathf.Lerp(0f, 8f, t / 0.5f)
                    : (t <= 3.5f) ? 8f
                    : Mathf.Lerp(8f, 0f, (t - 3.5f) / 0.5f);

            // x(t) cm
            float x = (t <= 1.0f) ? Mathf.Lerp(3f, 4f, t / 1.0f)
                    : (t <= 2.0f) ? Mathf.Lerp(4f, 3f, (t - 1.0f) / 1.0f)
                    : Mathf.Lerp(3f, 5f, (t - 2.0f) / 2.0f);

            float fingerTotal = (y <= 0f) ? 0f : (y < 1f ? y : 1f);
            float ratio = (x <= 3f) ? 0f : (x >= 5f ? 1f : (x - 3f) / 2f);

            float A_mm = fingerTotal * (1f - ratio) * 5f;
            float B_mm = fingerTotal * ratio * 5f;

            long A_enc = (long)(A_mm * ENCODER_PER_MM);
            long B_enc = (long)(B_mm * ENCODER_PER_MM);

            SendCommand('a', A_enc, B_enc);

            t += Time.deltaTime;
            yield return null;
        }

        SendStopCommand();
        handshakeCo = null;
    }
}
