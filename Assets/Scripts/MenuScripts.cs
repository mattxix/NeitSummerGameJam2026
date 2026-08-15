using UnityEngine;
using UnityEngine.SceneManagement;


public class MenuScripts : MonoBehaviour
{
    [Tooltip("Scene loaded when the player finishes the sandwich.")]
    public string mainMenuScene = "MainMenu";

    [Tooltip("Scene loaded when a seagull steals the sandwich.")]
    public string gameScene = "BirdAttack";

    public void Win()
    {
        SceneManager.LoadScene(0);
    }

    public void Lose()
    {
        SceneManager.LoadScene(0);
    }

    public void Start()
    {
        SceneManager.LoadScene(1);
    }
    public void Exit()
    {
        Application.Quit();
    }
}