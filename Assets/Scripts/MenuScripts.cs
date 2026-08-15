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
        SceneManager.LoadScene(mainMenuScene);
    }

    public void Lose()
    {
        SceneManager.LoadScene(gameScene);
    }
}