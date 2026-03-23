import pickle

def read_pickle(file_path):
    with open(file_path, 'rb') as f:
        data = pickle.load(f)
    return data

def data_process(data):
    world_pos = []
    for frame in data:
        temp_str = ""
        for idx, skeletons in enumerate(frame):
            temp_str += str(skeletons[0]) + " " + str(skeletons[1]) + " " + str(skeletons[2]) + ","
        temp_str = temp_str[:len(temp_str)-1]
        world_pos.append(temp_str)
        
    return world_pos

def save_txt(save_path, data):
    with open(save_path, 'w') as f:
        for line in data:
            f.write(line + "\n")
            
    print("Save txt file to", save_path)

if __name__ == '__main__':
    pkl_path = "C:\\Users\\screa\\NCKU\\gait-analysis\\thesis_output\\0508_p1\\test\\gait_3_alphapose_6_norm_kpts_3d.pkl"
    save_path = "../Motion Data/0508_p1_gait_3.txt"
    data = read_pickle(pkl_path)
    # remove the last two columns (toe points)
    data = data[:, :-2]
    
    txt_data = data_process(data)
    save_txt(save_path, txt_data)